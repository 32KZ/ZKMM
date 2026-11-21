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
    ///   everest_update.yaml       mod name -> the GameBanana file it is downloaded from
    ///   mod_search_database.yaml  GameBanana page -> its files, author, description and screenshots
    /// The two are joined on the file. (Older copies of the update list named the GameBanana page directly;
    /// that is still understood.) The result is kept in Everest's cache folder and refreshed once a week,
    /// in the background.
    /// Everything here is best effort: without a connection the menu simply shows no author.
    /// </summary>
    internal static class ModInfoDatabase {

        public struct Info {
            public string Author;
            public string Description;
            /// <summary>Addresses of the mod's first screenshot, best first. Never null.</summary>
            public string[] Images;
        }

        private const int MaxAgeDays = 7;
        private const string CacheHeader = "ZKMM-MODINFO-2";

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
                    string[] images = parts.Length > 3 && parts[3].Length > 0 ? parts[3].Split(' ') : Array.Empty<string>();
                    loaded[parts[0]] = new Info { Author = parts[1], Description = parts[2], Images = images };
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
                    writer.Write(Clean(pair.Value.Description));
                    writer.Write('\t');
                    writer.WriteLine(Clean(string.Join(" ", pair.Value.Images ?? Array.Empty<string>())));
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

                // "file:12345" (a GameBanana download) or "page:Mod/12345" -> names of the mods published there.
                Dictionary<string, List<string>> names = new Dictionary<string, List<string>>();
                Download(client, UpdateListUrls, reader => ReadUpdateList(reader, names));
                if (names.Count == 0)
                    throw new InvalidDataException("the mod list was empty");
                Logger.Log(LogLevel.Info, ZKModMenuModule.LogTag, "Read the mod list: " + names.Count + " downloads.");

                Dictionary<string, Info> result = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
                Download(client, SearchDatabaseUrls, reader => ReadSearchDatabase(reader, names, result));
                if (result.Count == 0)
                    throw new InvalidDataException("no mod in the database matched the mod list");

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
            /// <summary>The key this list or mapping is the value of (null for list entries and the document).</summary>
            public string Owner;
        }

        /// <summary>The nearest key that encloses the top of the stack, such as "Files" for a file's mapping.</summary>
        private static string Context(List<Frame> stack) {
            for (int i = stack.Count - 1; i >= 0; i--) {
                if (stack[i].Owner != null)
                    return stack[i].Owner;
            }
            return null;
        }

        /// <summary>
        /// Walk a YAML document without building it in memory (the mod database is large).
        /// Depth 1 is the outermost list or mapping.
        /// <paramref name="pair"/> gets (depth, key, value, context) for each key: value of a mapping, and
        /// <paramref name="item"/> gets (depth, context, value) for each plain value of a list, where context is
        /// the nearest enclosing key.
        /// </summary>
        private static void Walk(TextReader reader, Action<int, string> mapStart, Action<int, string, string, string> pair, Action<int> mapEnd,
            Action<int, string, string> item = null) {
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
                            pair(stack.Count, top.Key, scalar.Value, Context(stack));
                            top.ExpectKey = true;
                        }
                    } else if (top != null) {
                        item?.Invoke(stack.Count, Context(stack), scalar.Value);
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
                    stack.Add(new Frame { IsMap = isMap, ExpectKey = true, Owner = key });
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

        /// <summary>The GameBanana file id at the end of a download address such as https://gamebanana.com/mmdl/12345.</summary>
        internal static string FileKey(string url) {
            if (string.IsNullOrEmpty(url))
                return null;
            int slash = url.TrimEnd('/').LastIndexOf('/');
            string last = url.TrimEnd('/').Substring(slash + 1);
            if (last.Length == 0)
                return null;
            foreach (char c in last) {
                if (c < '0' || c > '9')
                    return null;
            }
            return "file:" + last;
        }

        private static void AddName(Dictionary<string, List<string>> names, string key, string name) {
            if (key == null)
                return;
            if (!names.TryGetValue(key, out List<string> list))
                names[key] = list = new List<string>();
            if (!list.Contains(name))
                list.Add(name);
        }

        // ModName:
        //   URL: https://gamebanana.com/mmdl/12345
        //   MirrorName: '12345'
        //   (older lists: GameBananaType: Mod, GameBananaId: 678)
        internal static void ReadUpdateList(TextReader reader, Dictionary<string, List<string>> names) {
            string name = null, type = null, id = null, url = null, mirror = null;
            Walk(reader,
                (depth, key) => {
                    if (depth == 2) {
                        name = key;
                        type = id = url = mirror = null;
                    }
                },
                (depth, key, value, context) => {
                    if (depth != 2)
                        return;
                    switch (key) {
                        case "GameBananaType":
                            type = value;
                            break;
                        case "GameBananaId":
                            id = value;
                            break;
                        case "URL":
                            url = value;
                            break;
                        case "MirrorName":
                            mirror = value;
                            break;
                    }
                },
                depth => {
                    if (depth != 2 || name == null)
                        return;
                    if (type != null && id != null)
                        AddName(names, "page:" + type + "/" + id, name);
                    string file = FileKey(url);
                    if (file == null && !string.IsNullOrEmpty(mirror))
                        file = FileKey("/" + mirror);
                    AddName(names, file, name);
                    name = null;
                });
        }

        /// <summary>"page:Mod/12345" from https://gamebanana.com/mods/12345.</summary>
        private static string PageKey(string pageUrl) {
            if (string.IsNullOrEmpty(pageUrl))
                return null;
            string[] parts = pageUrl.TrimEnd('/').Split('/');
            if (parts.Length < 2)
                return null;
            string section = parts[parts.Length - 2];
            string number = parts[parts.Length - 1];
            if (section.Length < 2 || number.Length == 0)
                return null;
            // "mods" -> "Mod", "tools" -> "Tool", "wips" -> "Wip"
            if (section.EndsWith("s"))
                section = section.Substring(0, section.Length - 1);
            section = char.ToUpperInvariant(section[0]) + section.Substring(1);
            return "page:" + section + "/" + number;
        }

        /// <summary>Only images the game can open (PNG and JPEG; GameBanana's WebP cannot be).</summary>
        private static bool CanOpen(string url) {
            if (string.IsNullOrEmpty(url))
                return false;
            string lower = url.ToLowerInvariant();
            return lower.StartsWith("https://") && (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg"));
        }

        // - GameBananaType: Mod            (older databases)
        //   GameBananaId: 12345            (older databases)
        //   PageURL: https://gamebanana.com/mods/12345
        //   Author: ...
        //   Description: ...
        //   Files:
        //   - URL: https://gamebanana.com/mmdl/67890
        //   MirroredScreenshots:
        //   - https://.../img_ss_mods_abc.png
        //   Screenshots:
        //   - https://images.gamebanana.com/img/ss/mods/abc.jpg
        internal static void ReadSearchDatabase(TextReader reader, Dictionary<string, List<string>> names, Dictionary<string, Info> result) {
            string type = null, id = null, page = null, author = null, description = null;
            List<string> files = new List<string>();
            string mirrored = null, original = null;

            Walk(reader,
                (depth, key) => {
                    if (depth == 2) {
                        type = id = page = author = description = mirrored = original = null;
                        files.Clear();
                    }
                },
                (depth, key, value, context) => {
                    if (depth == 2) {
                        switch (key) {
                            case "GameBananaType":
                                type = value;
                                break;
                            case "GameBananaId":
                                id = value;
                                break;
                            case "PageURL":
                                page = value;
                                break;
                            case "Author":
                                author = value;
                                break;
                            case "Description":
                                description = value;
                                break;
                        }
                    } else if (depth == 4 && context == "Files" && key == "URL") {
                        // A file of the entry.
                        string file = FileKey(value);
                        if (file != null)
                            files.Add(file);
                    }
                },
                depth => {
                    if (depth != 2)
                        return;

                    List<string> matched = new List<string>();
                    void Match(string key) {
                        if (key != null && names.TryGetValue(key, out List<string> found)) {
                            foreach (string n in found) {
                                if (!matched.Contains(n))
                                    matched.Add(n);
                            }
                        }
                    }
                    foreach (string file in files)
                        Match(file);
                    if (type != null && id != null)
                        Match("page:" + type + "/" + id);
                    Match(PageKey(page));
                    if (matched.Count == 0)
                        return;

                    List<string> images = new List<string>();
                    if (CanOpen(mirrored))
                        images.Add(mirrored);
                    if (CanOpen(original))
                        images.Add(original);
                    Info info = new Info { Author = Clean(author), Description = Clean(description), Images = images.ToArray() };
                    foreach (string name in matched)
                        result[name] = info;
                },
                (depth, context, value) => {
                    // Plain values inside a list: the screenshots. The first the game can open of each list is kept.
                    if (depth != 3)
                        return;
                    if (context == "MirroredScreenshots" && mirrored == null && CanOpen(value))
                        mirrored = value;
                    else if (context == "Screenshots" && original == null && CanOpen(value))
                        original = value;
                });
        }
    }
}
