using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace ZKModMenu {
    /// <summary>
    /// Author and description of mods.
    ///
    /// Everest keeps neither (a mod's everest.yaml only has its name and version), so they come from
    /// the public Celeste mod database that Everest's own mod updater and Olympus use:
    ///   everest_update.yaml       mod name -> GameBanana id
    ///   mod_search_database.yaml  GameBanana id -> name, author, description
    /// The result is kept in Everest's cache folder and refreshed once a week, in the background.
    /// Everything here is best effort: without a connection the menu simply shows no author.
    /// </summary>
    internal static class ModInfoDatabase {

        public struct Info {
            public string Author;
            public string Description;
        }

        private const int MaxAgeDays = 7;
        private const string CacheHeader = "ZKMM-MODINFO-1";

        private static readonly string[] UpdateListUrls = {
            "https://maddie480.ovh/celeste/everest_update.yaml",
            "https://everestapi.github.io/updatermirror/everest_update.yaml"
        };

        private static readonly string[] SearchDatabaseUrls = {
            "https://maddie480.ovh/celeste/mod_search_database.yaml",
            "https://everestapi.github.io/updatermirror/mod_search_database.yaml"
        };

        private static volatile Dictionary<string, Info> infos = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
        private static bool cacheLoaded;
        private static DateTime cacheTime = DateTime.MinValue;
        private static int refreshing;
        private static DateTime lastAttempt = DateTime.MinValue;

        /// <summary>Goes up every time fresh data arrives, so callers know to look again.</summary>
        public static int Version { get; private set; }

        private static string CachePath {
            get {
                string folder = Everest.Loader.PathCache;
                if (string.IsNullOrEmpty(folder))
                    folder = Path.Combine(Everest.PathGame, "Mods", "Cache");
                return Path.Combine(folder, "ZKModMenu.modinfo.txt");
            }
        }

        /// <summary>
        /// Make the data available: read the cache and, when it is old and <paramref name="allowDownload"/> is set,
        /// start a refresh in the background. Cheap to call often.
        /// </summary>
        public static void Prepare(bool allowDownload) {
            if (!cacheLoaded) {
                cacheLoaded = true;
                try {
                    LoadCache();
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not read the mod info cache: " + e.Message);
                }
            }

            if (!allowDownload)
                return;
            if ((DateTime.UtcNow - cacheTime).TotalDays < MaxAgeDays)
                return;
            // After a failed attempt, wait a while before trying again.
            if ((DateTime.UtcNow - lastAttempt).TotalMinutes < 30)
                return;
            if (Interlocked.CompareExchange(ref refreshing, 1, 0) != 0)
                return;

            lastAttempt = DateTime.UtcNow;
            Task.Run(() => {
                try {
                    Refresh();
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not download mod authors and descriptions: " + e.Message);
                } finally {
                    Interlocked.Exchange(ref refreshing, 0);
                }
            });
        }

        public static bool TryGet(string modName, out Info info) {
            if (modName == null) {
                info = default;
                return false;
            }
            return infos.TryGetValue(modName, out info);
        }

        // ------------------------------------------------------------------ cache file

        private static string Clean(string text) {
            if (string.IsNullOrEmpty(text))
                return "";
            StringBuilder builder = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text) {
                if (c == '\t' || c == '\r' || c == '\n' || c == ' ') {
                    space = builder.Length > 0;
                    continue;
                }
                if (char.IsControl(c))
                    continue;
                if (space) {
                    builder.Append(' ');
                    space = false;
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static void LoadCache() {
            string path = CachePath;
            if (!File.Exists(path))
                return;

            Dictionary<string, Info> loaded = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8)) {
                string header = reader.ReadLine();
                if (header == null)
                    return;
                string[] head = header.Split('\t');
                if (head.Length < 2 || head[0] != CacheHeader || !long.TryParse(head[1], out long ticks))
                    return;

                string line;
                while ((line = reader.ReadLine()) != null) {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3 || parts[0].Length == 0)
                        continue;
                    loaded[parts[0]] = new Info { Author = parts[1], Description = parts[2] };
                }
                cacheTime = new DateTime(ticks, DateTimeKind.Utc);
            }

            infos = loaded;
            Version++;
        }

        private static void SaveCache(Dictionary<string, Info> data, DateTime time) {
            string path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false))) {
                writer.Write(CacheHeader);
                writer.Write('\t');
                writer.WriteLine(time.Ticks);
                foreach (KeyValuePair<string, Info> pair in data) {
                    writer.Write(Clean(pair.Key));
                    writer.Write('\t');
                    writer.Write(Clean(pair.Value.Author));
                    writer.Write('\t');
                    writer.WriteLine(Clean(pair.Value.Description));
                }
            }
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        // ------------------------------------------------------------------ download

        private static void Refresh() {
            using (HttpClientHandler handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (HttpClient client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) }) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ZKModMenu/" + ZKModMenuModule.VersionText);

                // GameBanana page ("Mod/12345") -> names of the mods published there.
                Dictionary<string, List<string>> names = new Dictionary<string, List<string>>();
                Download(client, UpdateListUrls, reader => ReadUpdateList(reader, names));
                if (names.Count == 0)
                    throw new InvalidDataException("the mod list was empty");

                Dictionary<string, Info> result = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
                Download(client, SearchDatabaseUrls, reader => ReadSearchDatabase(reader, names, result));
                if (result.Count == 0)
                    throw new InvalidDataException("the mod database was empty");

                DateTime now = DateTime.UtcNow;
                infos = result;
                cacheTime = now;
                Version++;
                Logger.Log(LogLevel.Info, ZKModMenuModule.LogTag, "Loaded the author and description of " + result.Count + " mods.");

                try {
                    SaveCache(result, now);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not save the mod info cache: " + e.Message);
                }
            }
        }

        private static void Download(HttpClient client, string[] urls, Action<TextReader> read) {
            Exception last = null;
            foreach (string url in urls) {
                try {
                    using (HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()) {
                        response.EnsureSuccessStatusCode();
                        using (Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            read(reader);
                    }
                    return;
                } catch (Exception e) {
                    last = e;
                }
            }
            throw last ?? new IOException("no address to download from");
        }

        // ------------------------------------------------------------------ yaml

        private sealed class Frame {
            public bool IsMap;
            public bool ExpectKey;
            public string Key;
        }

        /// <summary>
        /// Walk a YAML document without building it in memory (the mod database is large).
        /// Depth 1 is the outermost list or mapping.
        /// </summary>
        private static void Walk(TextReader reader, Action<int, string> mapStart, Action<int, string, string> pair, Action<int> mapEnd) {
            Parser parser = new Parser(reader);
            List<Frame> stack = new List<Frame>();

            while (parser.MoveNext()) {
                ParsingEvent current = parser.Current;
                Frame top = stack.Count > 0 ? stack[stack.Count - 1] : null;

                if (current is Scalar scalar) {
                    if (top != null && top.IsMap) {
                        if (top.ExpectKey) {
                            top.Key = scalar.Value;
                            top.ExpectKey = false;
                        } else {
                            pair(stack.Count, top.Key, scalar.Value);
                            top.ExpectKey = true;
                        }
                    }
                } else if (current is AnchorAlias) {
                    if (top != null && top.IsMap) {
                        if (top.ExpectKey)
                            top.Key = null;
                        top.ExpectKey = !top.ExpectKey;
                    }
                } else if (current is MappingStart || current is SequenceStart) {
                    string key = null;
                    if (top != null && top.IsMap) {
                        key = top.ExpectKey ? null : top.Key;
                        top.ExpectKey = true;
                    }
                    bool isMap = current is MappingStart;
                    stack.Add(new Frame { IsMap = isMap, ExpectKey = true });
                    if (isMap)
                        mapStart(stack.Count, key);
                } else if (current is MappingEnd) {
                    mapEnd(stack.Count);
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                } else if (current is SequenceEnd) {
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        // ModName:
        //   GameBananaType: Mod
        //   GameBananaId: 12345
        private static void ReadUpdateList(TextReader reader, Dictionary<string, List<string>> names) {
            string name = null, type = null, id = null;
            Walk(reader,
                (depth, key) => {
                    if (depth == 2) {
                        name = key;
                        type = id = null;
                    }
                },
                (depth, key, value) => {
                    if (depth != 2)
                        return;
                    if (key == "GameBananaType")
                        type = value;
                    else if (key == "GameBananaId")
                        id = value;
                },
                depth => {
                    if (depth != 2 || name == null || type == null || id == null)
                        return;
                    string page = type + "/" + id;
                    if (!names.TryGetValue(page, out List<string> list))
                        names[page] = list = new List<string>();
                    list.Add(name);
                    name = null;
                });
        }

        // - GameBananaType: Mod
        //   GameBananaId: 12345
        //   Name: ...
        //   Author: ...
        //   Description: ...
        private static void ReadSearchDatabase(TextReader reader, Dictionary<string, List<string>> names, Dictionary<string, Info> result) {
            string type = null, id = null, author = null, description = null;
            Walk(reader,
                (depth, key) => {
                    if (depth == 2)
                        type = id = author = description = null;
                },
                (depth, key, value) => {
                    if (depth != 2)
                        return;
                    switch (key) {
                        case "GameBananaType":
                            type = value;
                            break;
                        case "GameBananaId":
                            id = value;
                            break;
                        case "Author":
                            author = value;
                            break;
                        case "Description":
                            description = value;
                            break;
                    }
                },
                depth => {
                    if (depth != 2 || type == null || id == null)
                        return;
                    if (!names.TryGetValue(type + "/" + id, out List<string> list))
                        return;
                    Info info = new Info { Author = Clean(author), Description = Clean(description) };
                    foreach (string name in list)
                        result[name] = info;
                });
        }
    }
}
