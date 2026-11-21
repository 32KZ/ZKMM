using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod;
using Microsoft.Xna.Framework.Graphics;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>
    /// Each mod's picture: the first screenshot of its page in the public Celeste mod database
    /// (see <see cref="ModInfoDatabase"/>).
    ///
    /// Pictures are fetched only when a mod is on screen, two at a time, in the background. Each is shrunk once
    /// and the small copy is kept in Everest's cache folder, so it is downloaded only once. A limited number
    /// stay in memory; the least recently shown are let go first.
    /// </summary>
    internal static class ModImages {
        public const int BannerW = 320, BannerH = 180, ThumbSize = 112;
        private const int MaxInMemory = 40;
        private const int MaxDownloadBytes = 6 * 1024 * 1024;
        private const int MaxParallel = 2;
        private const string CacheMagic = "ZKIMG1";

        private enum State {
            Idle,
            Queued,
            Loading,
            Ready,
            Failed
        }

        private sealed class Entry {
            public string Id;
            public State State;
            public ZkImage Thumb, Banner;
            public long LastUsed;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<Entry> Queue = new LinkedList<Entry>();
        private static readonly object Lock = new object();
        private static readonly object DecodeLock = new object();
        private static int running;
        private static long clock;
        private static HttpClient client;

        private static string CacheFolder {
            get {
                string folder = Everest.Loader.PathCache;
                if (string.IsNullOrEmpty(folder))
                    folder = Path.Combine(Everest.PathGame, "Mods", "Cache");
                return Path.Combine(folder, "ZKModMenu-images");
            }
        }

        /// <summary>
        /// The picture of a mod, or nulls while it is not there (yet). Asking starts loading it.
        /// Call from the main thread only.
        /// </summary>
        public static void Get(string id, bool allowDownload, out ZkImage thumb, out ZkImage banner) {
            thumb = banner = null;
            if (string.IsNullOrEmpty(id))
                return;

            lock (Lock) {
                if (!Entries.TryGetValue(id, out Entry entry)) {
                    entry = new Entry { Id = id };
                    Entries[id] = entry;
                }
                entry.LastUsed = ++clock;

                switch (entry.State) {
                    case State.Ready:
                        thumb = entry.Thumb;
                        banner = entry.Banner;
                        return;
                    case State.Idle:
                        // Without an address there is nothing to fetch; ask again once the database has arrived.
                        if (!ModInfoDatabase.TryGet(id, out ModInfoDatabase.Info info) || info.Images == null || info.Images.Length == 0)
                            return;
                        entry.State = State.Queued;
                        Queue.AddFirst(entry);
                        break;
                    case State.Queued:
                        // Shown again: move it to the front, so what is on screen now comes first.
                        Queue.Remove(entry);
                        Queue.AddFirst(entry);
                        break;
                }
            }
            Pump(allowDownload);
        }

        /// <summary>Forget what failed, so it is tried again (after the database or the settings changed).</summary>
        public static void RetryFailed() {
            lock (Lock) {
                foreach (Entry entry in Entries.Values) {
                    if (entry.State == State.Failed)
                        entry.State = State.Idle;
                }
            }
        }

        private static void Pump(bool allowDownload) {
            while (true) {
                Entry next;
                lock (Lock) {
                    if (running >= MaxParallel || Queue.Count == 0)
                        return;
                    next = Queue.First.Value;
                    Queue.RemoveFirst();
                    next.State = State.Loading;
                    running++;
                }
                Entry entry = next;
                Task.Run(() => {
                    ZkImage thumb = null, banner = null;
                    try {
                        Load(entry.Id, allowDownload, out thumb, out banner);
                    } catch (Exception e) {
                        Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "No picture for " + entry.Id + ": " + e.Message);
                    }
                    lock (Lock) {
                        running--;
                        if (banner != null && thumb != null) {
                            entry.Thumb = thumb;
                            entry.Banner = banner;
                            entry.State = State.Ready;
                        } else {
                            entry.State = State.Failed;
                        }
                    }
                });
            }
        }

        /// <summary>Let go of the pictures shown least recently, keeping at most <see cref="MaxInMemory"/>. Main thread only.</summary>
        public static void Trim() {
            List<Entry> ready = new List<Entry>();
            lock (Lock) {
                foreach (Entry entry in Entries.Values) {
                    if (entry.State == State.Ready)
                        ready.Add(entry);
                }
                if (ready.Count <= MaxInMemory)
                    return;
                ready.Sort((a, b) => a.LastUsed.CompareTo(b.LastUsed));
                for (int i = 0; i < ready.Count - MaxInMemory; i++) {
                    Entry entry = ready[i];
                    MonocleCanvas.Forget(entry.Thumb);
                    MonocleCanvas.Forget(entry.Banner);
                    entry.Thumb = entry.Banner = null;
                    // Comes back from the cache folder when it is shown again.
                    entry.State = State.Idle;
                }
            }
        }

        // ------------------------------------------------------------------ loading (background)

        private static string CachePath(string id) {
            using (SHA1 sha = SHA1.Create()) {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(id.ToLowerInvariant()));
                StringBuilder name = new StringBuilder();
                for (int i = 0; i < 10; i++)
                    name.Append(hash[i].ToString("x2"));
                return Path.Combine(CacheFolder, name + ".zkimg");
            }
        }

        private static void Load(string id, bool allowDownload, out ZkImage thumb, out ZkImage banner) {
            thumb = banner = null;
            ModInfoDatabase.TryGet(id, out ModInfoDatabase.Info info);
            string[] urls = info.Images ?? Array.Empty<string>();
            string path = CachePath(id);

            // The small copy made last time, as long as it is of the same picture.
            if (ReadCache(path, urls, out byte[] rgba, out int width, out int height)) {
                Make(rgba, width, height, out thumb, out banner);
                return;
            }
            if (!allowDownload || urls.Length == 0)
                return;

            foreach (string url in urls) {
                byte[] file;
                try {
                    file = Download(url);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not download " + url + ": " + e.Message);
                    continue;
                }
                if (!Decode(file, out byte[] pixels, out int w, out int h))
                    continue;

                // Shrink once to the banner's size (cropped to 16:9) and keep that.
                ZkImage small = ZkImage.Cover(pixels, w, h, BannerW, BannerH, 0f);
                if (small == null)
                    continue;
                byte[] smallRgba = ToRgba(small);
                try {
                    WriteCache(path, urls, smallRgba, small.Width, small.Height);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not keep the picture of " + id + ": " + e.Message);
                }
                Make(smallRgba, small.Width, small.Height, out thumb, out banner);
                return;
            }
        }

        private static void Make(byte[] rgba, int width, int height, out ZkImage thumb, out ZkImage banner) {
            // Corner radii match the letter badges they stand in for.
            banner = ZkImage.Cover(rgba, width, height, BannerW, BannerH, 0.23f);
            thumb = ZkImage.Cover(rgba, width, height, ThumbSize, ThumbSize, 0.27f);
        }

        private static byte[] ToRgba(ZkImage image) {
            byte[] rgba = new byte[image.Width * image.Height * 4];
            for (int i = 0; i < image.Pixels.Length; i++) {
                ZkColor c = image.Pixels[i];
                rgba[i * 4] = c.R;
                rgba[i * 4 + 1] = c.G;
                rgba[i * 4 + 2] = c.B;
                rgba[i * 4 + 3] = c.A;
            }
            return rgba;
        }

        private static byte[] Download(string url) {
            HttpClient http = client;
            if (http == null) {
                HttpClientHandler handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
                http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ZKModMenu/" + ZKModMenuModule.VersionText);
                if (Interlocked.CompareExchange(ref client, http, null) != null) {
                    http.Dispose();
                    http = client;
                }
            }

            using (HttpResponseMessage response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()) {
                response.EnsureSuccessStatusCode();
                long? length = response.Content.Headers.ContentLength;
                if (length > MaxDownloadBytes)
                    throw new InvalidDataException("the picture is too large");
                using (Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (MemoryStream memory = new MemoryStream()) {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                        memory.Write(buffer, 0, read);
                        if (memory.Length > MaxDownloadBytes)
                            throw new InvalidDataException("the picture is too large");
                    }
                    return memory.ToArray();
                }
            }
        }

        /// <summary>PNG or JPEG to RGBA, with the decoder the game itself uses for its textures. No graphics device needed.</summary>
        private static bool Decode(byte[] file, out byte[] rgba, out int width, out int height) {
            rgba = null;
            width = height = 0;
            try {
                lock (DecodeLock) {
                    using (MemoryStream stream = new MemoryStream(file))
                        Texture2D.TextureDataFromStreamEXT(stream, out width, out height, out rgba);
                }
                return rgba != null && width > 0 && height > 0 && rgba.Length >= width * height * 4;
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not open a picture: " + e.Message);
                return false;
            }
        }

        // Cache file: magic, the picture's addresses (so a new picture replaces it), size, then gzipped RGBA.
        private static bool ReadCache(string path, string[] urls, out byte[] rgba, out int width, out int height) {
            rgba = null;
            width = height = 0;
            try {
                if (!File.Exists(path))
                    return false;
                using (FileStream file = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(file)) {
                    if (reader.ReadString() != CacheMagic)
                        return false;
                    string source = reader.ReadString();
                    // Offline, the database may not know the addresses; the picture kept is still the best there is.
                    if (urls.Length > 0 && source != string.Join(" ", urls))
                        return false;
                    width = reader.ReadInt32();
                    height = reader.ReadInt32();
                    if (width <= 0 || height <= 0 || width > 2048 || height > 2048)
                        return false;
                    rgba = new byte[width * height * 4];
                    using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress)) {
                        int at = 0;
                        while (at < rgba.Length) {
                            int read = gzip.Read(rgba, at, rgba.Length - at);
                            if (read <= 0)
                                return false;
                            at += read;
                        }
                    }
                    return true;
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not read a kept picture: " + e.Message);
                return false;
            }
        }

        private static void WriteCache(string path, string[] urls, byte[] rgba, int width, int height) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp" + Environment.CurrentManagedThreadId;
            using (FileStream file = File.Create(temp)) {
                using (BinaryWriter writer = new BinaryWriter(file, Encoding.UTF8, true)) {
                    writer.Write(CacheMagic);
                    writer.Write(string.Join(" ", urls));
                    writer.Write(width);
                    writer.Write(height);
                }
                using (GZipStream gzip = new GZipStream(file, CompressionLevel.Fastest, true))
                    gzip.Write(rgba, 0, rgba.Length);
            }
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }
    }
}
