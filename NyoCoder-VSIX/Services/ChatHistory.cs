using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NyoCoder
{
    /// <summary>
    /// A saved chat: the conversation as the model sees it, which also redraws the chat
    /// on load. Plan steps keep only the label and result the main conversation keeps.
    /// </summary>
    internal sealed class ChatSessionData
    {
        public string Title;
        public string Workspace;
        public string Mode;
        public List<LLMClient.ChatMessage> Messages = new List<LLMClient.ChatMessage>();
    }

    /// <summary>One saved chat in the history list.</summary>
    public sealed class ChatHistoryEntry : INotifyPropertyChanged
    {
        private bool _isCurrent;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Path { get; set; }
        public string Title { get; set; }

        // Last save time: orders the list newest first. Not shown.
        public DateTime Modified { get; set; }

        // What UI Automation (screen readers) reads for a list item.
        public override string ToString()
        {
            return Title;
        }

        /// <summary>The chat the window shows; highlighted whatever is selected or focused.</summary>
        public bool IsCurrent
        {
            get { return _isCurrent; }
            set
            {
                if (_isCurrent == value)
                    return;
                _isCurrent = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                    handler(this, new PropertyChangedEventArgs("IsCurrent"));
            }
        }
    }

    /// <summary>
    /// The auto-saved chats, one folder per workspace: %AppData%\NyoCoder\chats\&lt;folder
    /// name&gt;-&lt;hash&gt;\. The hash of the full path keeps two workspaces with the same
    /// folder name apart. The file format matches SimpleLLMChat's, plus the mode.
    /// </summary>
    internal static class ChatHistory
    {
        private const int TitleMaxLength = 60;

        public static string ChatsRoot
        {
            get
            {
                return Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NyoCoder"), "chats");
            }
        }

        /// <summary>The folder holding <paramref name="workspace"/>'s chats.</summary>
        public static string DirectoryFor(string workspace)
        {
            return Path.Combine(ChatsRoot, FolderName(workspace));
        }

        /// <summary>e.g. C:\Projects\MyApp → MyApp-3fa2c1d0.</summary>
        private static string FolderName(string workspace)
        {
            string full = Normalize(workspace);
            string name = Path.GetFileName(full);
            if (string.IsNullOrEmpty(name))
                name = full.Substring(0, 1);    // A drive root: its letter.
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            // Windows paths are case-insensitive: C:\Work and c:\work are one workspace.
            byte[] hash;
            using (SHA1 sha = SHA1.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
            StringBuilder suffix = new StringBuilder(8);
            for (int i = 0; i < 4; i++)
                suffix.Append(hash[i].ToString("x2"));

            return name + "-" + suffix;
        }

        /// <summary>Full path without a trailing separator (a drive root keeps its own).</summary>
        private static string Normalize(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return full.Length > root.Length
                ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : full;
        }

        /// <summary>
        /// The folder whose chats the history shows: the open solution's, as the codebase
        /// index resolves it. Null when it can't be resolved.
        /// </summary>
        public static string CurrentWorkspace()
        {
            try
            {
                string solutionKey, workspaceRoot;
                if (CodebaseIndex.ResolveWorkspace(out solutionKey, out workspaceRoot)
                    && !string.IsNullOrEmpty(workspaceRoot))
                    return Normalize(workspaceRoot);
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>A file name from the start time, e.g. 20261123-221530.json.</summary>
        public static string NewSessionPath(string workspace)
        {
            string directory = DirectoryFor(workspace);
            string stem = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, stem + ".json");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(directory, stem + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
            return path;
        }

        /// <summary>
        /// Deletes the oldest chats in <paramref name="currentPath"/>'s folder beyond
        /// <paramref name="keep"/>, never the current one: the limit is per workspace.
        /// </summary>
        public static void Prune(int keep, string currentPath)
        {
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(currentPath));
            if (!directory.Exists)
                return;

            FileInfo[] sessions = directory.GetFiles("*.json");
            Array.Sort(sessions, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            int kept = 0;
            foreach (FileInfo session in sessions)
            {
                bool current = string.Equals(session.FullName, currentPath, StringComparison.OrdinalIgnoreCase);
                if (current || kept < keep)
                {
                    kept++;
                    continue;
                }
                try { session.Delete(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Saved chats in <paramref name="directory"/>, newest first.</summary>
        public static List<ChatHistoryEntry> Read(string directory)
        {
            List<ChatHistoryEntry> entries = new List<ChatHistoryEntry>();
            if (!Directory.Exists(directory))
                return entries;

            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                entries.Add(new ChatHistoryEntry
                {
                    Path = path,
                    Title = ReadTitle(path) ?? Path.GetFileNameWithoutExtension(path),
                    Modified = File.GetLastWriteTime(path)
                });
            }

            entries.Sort((a, b) => b.Modified.CompareTo(a.Modified));
            return entries;
        }

        /// <summary>
        /// Reads only the top-level "title" property. Save writes it first, so the rest
        /// of the file (which can hold inline images) is never parsed.
        /// </summary>
        private static string ReadTitle(string path)
        {
            try
            {
                // Share everything, Delete included: a save swaps a new file over this one.
                using (FileStream file = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader stream = new StreamReader(file, Encoding.UTF8))
                using (JsonTextReader reader = new JsonTextReader(stream))
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                        return null;

                    while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                    {
                        if ((string)reader.Value == "title")
                        {
                            string title = reader.ReadAsString();
                            return TextNormalization.IsNullOrWhiteSpace(title) ? null : title;
                        }
                        reader.Skip();
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable or partly written: list it under its file name.
            }
            return null;
        }

        public static void Save(string path, ChatSessionData chat)
        {
            JArray messages = new JArray();
            foreach (LLMClient.ChatMessage message in chat.Messages)
                messages.Add(MessageToJson(message));

            // Small fields first: the history list reads the title without parsing the rest.
            JObject root = new JObject();
            if (!string.IsNullOrEmpty(chat.Title))
                root["title"] = chat.Title;
            if (!string.IsNullOrEmpty(chat.Workspace))
                root["workingDirectory"] = chat.Workspace;
            if (!string.IsNullOrEmpty(chat.Mode))
                root["mode"] = chat.Mode;
            root["messages"] = messages;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AtomicFile.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(root.ToString(Formatting.Indented) + "\n"));
        }

        public static ChatSessionData Load(string path)
        {
            JObject root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            ChatSessionData chat = new ChatSessionData
            {
                Title = (string)root["title"],
                Workspace = (string)root["workingDirectory"],
                Mode = (string)root["mode"]
            };

            JArray messages = root["messages"] as JArray ?? new JArray();
            foreach (JObject obj in messages)
                chat.Messages.Add(MessageFromJson(obj));
            return chat;
        }

        private static JObject MessageToJson(LLMClient.ChatMessage message)
        {
            JObject obj = new JObject();
            obj["role"] = message.Role ?? string.Empty;
            obj["content"] = message.Content ?? string.Empty;

            if (!string.IsNullOrEmpty(message.Context))
                obj["context"] = message.Context;

            if (!string.IsNullOrEmpty(message.ToolCallId))
                obj["tool_call_id"] = message.ToolCallId;

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                JArray calls = new JArray();
                foreach (ToolHandler.ToolCall call in message.ToolCalls)
                {
                    JObject callObj = new JObject();
                    callObj["id"] = call.Id ?? string.Empty;
                    callObj["name"] = call.Name ?? string.Empty;
                    callObj["arguments"] = call.Arguments ?? string.Empty;
                    calls.Add(callObj);
                }
                obj["tool_calls"] = calls;
            }

            // Inline so the file is self-contained; the source image may be gone by load time.
            if (message.Image != null)
            {
                obj["image"] = message.Image;
                obj["image_mime"] = string.IsNullOrEmpty(message.ImageMime) ? "image/png" : message.ImageMime;
            }

            return obj;
        }

        private static LLMClient.ChatMessage MessageFromJson(JObject obj)
        {
            LLMClient.ChatMessage message = new LLMClient.ChatMessage(
                (string)obj["role"] ?? string.Empty, (string)obj["content"] ?? string.Empty);
            message.Context = (string)obj["context"];
            message.ToolCallId = (string)obj["tool_call_id"];
            message.Image = (string)obj["image"];
            message.ImageMime = (string)obj["image_mime"];

            JArray calls = obj["tool_calls"] as JArray;
            if (calls != null)
            {
                foreach (JObject call in calls)
                {
                    message.ToolCalls.Add(new ToolHandler.ToolCall(
                        (string)call["name"] ?? string.Empty,
                        (string)call["arguments"] ?? string.Empty,
                        (string)call["id"] ?? string.Empty));
                }
            }
            return message;
        }

        /// <summary>
        /// A one-line title from the prompt the user typed: whitespace collapsed,
        /// cut at a word boundary. Null when there is no text.
        /// </summary>
        public static string MakeTitle(string prompt)
        {
            string text = Regex.Replace(prompt ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length == 0)
                return null;
            if (text.Length <= TitleMaxLength)
                return text;

            int cut = text.LastIndexOf(' ', TitleMaxLength);
            if (cut < TitleMaxLength / 2)
                cut = TitleMaxLength;
            return text.Substring(0, cut).TrimEnd() + "...";
        }

        /// <summary>
        /// The model's title answer as a title: thinking removed, first line only,
        /// without quotes, a "Title:" label or end punctuation. Null when nothing is left.
        /// </summary>
        public static string CleanModelTitle(string answer)
        {
            string text = Regex.Replace(answer ?? string.Empty, @"<think>.*?</think>", " ",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (string line in text.Split('\n'))
            {
                string title = line.Trim().Trim('"', '\'', '*', '`', '#').Trim();
                title = Regex.Replace(title, @"^title\s*:\s*", "", RegexOptions.IgnoreCase);
                title = title.Trim('"', '\'', '*', '`').TrimEnd('.', '!', '?', ':', ';').Trim();
                if (title.Length > 0)
                    return MakeTitle(title);
            }
            return null;
        }
    }
}
