using System;
using System.IO;
using System.Threading;

namespace NyoCoder
{
    /// <summary>
    /// Writes beside the target, flushes to disk, then swaps it into place, so the
    /// file is always the old or the new version, never a partial one. Saved chats
    /// are rewritten whole after every turn: a plain overwrite cut short (VS closing
    /// mid-save) could lose the whole chat.
    /// </summary>
    internal static class AtomicFile
    {
        public static void WriteAllBytes(string path, byte[] contents)
        {
            string target = Path.GetFullPath(path);
            string temp = target + ".tmp";

            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents, 0, contents.Length);
#if NET35
                stream.Flush();
#else
                stream.Flush(true);
#endif
            }

            // A reader (the history list, antivirus) can hold the target open for
            // a moment and block the swap; retry briefly before giving up.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(target))
                        File.Replace(temp, target, null);
                    else
                        File.Move(temp, target);
                    return;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException) || attempt >= 5)
                    {
                        try { File.Delete(temp); } catch (Exception) { }
                        throw;
                    }
                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }
}
