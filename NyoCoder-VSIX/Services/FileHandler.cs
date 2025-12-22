using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
public static class FileHandler
{
    public static string ReadFile(string filename, out int exitCode, int lineOffset = 0)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

            if (!File.Exists(filename))
            {
                exitCode = 1;
                return "File not found: " + filename;
            }

            // Validate offset
            if (lineOffset < 0)
                lineOffset = 0;

            List<string> linesToReturn = new List<string>();
            int totalLines = 0;
            int currentLineIndex = 0;

            // Read file line by line
            using (StreamReader reader = new StreamReader(filename, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    totalLines++;
                    
                    // Skip lines before the offset
                    if (currentLineIndex < lineOffset)
                    {
                        currentLineIndex++;
                        continue;
                    }

                    // Check if we've reached the limit
                    if (linesToReturn.Count >= ConfigHandler.MaxReadLines)
                    {
                        break;
                    }

                    linesToReturn.Add(line);
                    currentLineIndex++;
                }
            }

            // Validate offset
            if (lineOffset >= totalLines)
            {
                exitCode = 1;
                return "File has " + totalLines + " lines (0-indexed). Line offset " + lineOffset + " exceeds file length.";
            }

            // Build result with header
            StringBuilder result = new StringBuilder();
            int linesRead = linesToReturn.Count;
            int endLine = lineOffset + linesRead - 1;
            result.AppendLine("File has " + totalLines + " lines, reading lines " + lineOffset + "-" + endLine);
            result.AppendLine("---");
            
            // Join lines with newlines (ReadLine removes them, so we add them back)
            for (int i = 0; i < linesToReturn.Count; i++)
            {
                result.Append(linesToReturn[i]);
                if (i < linesToReturn.Count - 1)
                {
                    result.AppendLine();
                }
            }

            // Check if we truncated (either hit max lines or stopped early)
            bool wasTruncated = (lineOffset + linesRead < totalLines) || (linesRead >= ConfigHandler.MaxReadLines && currentLineIndex < totalLines);
            if (wasTruncated)
            {
                result.AppendLine();
                result.AppendLine("...[truncated]");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error reading file: " + ex.Message;
        }
    }

    public static string WriteFile(string filename, string content, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

            // Check if file is new (didn't exist before)
            bool isNewFile = !File.Exists(filename);

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(filename);
            string errorMessage;
            if (!EnsureDirectoryExists(directory, out exitCode, out errorMessage))
            {
                return errorMessage;
            }

            File.WriteAllText(filename, content, Encoding.UTF8);

            // If it's a new file, try to open it in Visual Studio
            if (isNewFile)
            {
                EditorService.TryOpenFileInVisualStudio(filename);
            }

            return "File written successfully: " + filename;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error writing file: " + ex.Message;
        }
    }

    public static string MoveFile(string sourcePath, string destinationPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Validate paths and prepare for file operation
            string errorMessage;
            if (!ValidateFileOperationPaths(ref sourcePath, ref destinationPath, out exitCode, out errorMessage))
            {
                return errorMessage;
            }

            // Move the file
            File.Move(sourcePath, destinationPath);
            
            return "File moved successfully from '" + sourcePath + "' to '" + destinationPath + "'";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error moving file: " + ex.Message;
        }
    }

    public static string CopyFile(string sourcePath, string destinationPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Validate paths and prepare for file operation
            string errorMessage;
            if (!ValidateFileOperationPaths(ref sourcePath, ref destinationPath, out exitCode, out errorMessage))
            {
                return errorMessage;
            }

            // Copy the file
            File.Copy(sourcePath, destinationPath);
            
            return "File copied successfully from '" + sourcePath + "' to '" + destinationPath + "'";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error copying file: " + ex.Message;
        }
    }

    // Shared validation for file operations (move, copy, etc.)
    private static bool ValidateFileOperationPaths(ref string sourcePath, ref string destinationPath, out int exitCode, out string errorMessage)
    {
        exitCode = 0;
        errorMessage = null;

        // Expand environment variables in both paths
        sourcePath = Environment.ExpandEnvironmentVariables(sourcePath);
        destinationPath = Environment.ExpandEnvironmentVariables(destinationPath);

        // Check if source file exists
        if (!File.Exists(sourcePath))
        {
            exitCode = 1;
            errorMessage = "Source file not found: " + sourcePath;
            return false;
        }

        // Check if destination file already exists
        if (File.Exists(destinationPath))
        {
            exitCode = 1;
            errorMessage = "Destination file already exists: " + destinationPath;
            return false;
        }

        // Ensure the destination directory exists
        string destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!EnsureDirectoryExists(destinationDirectory, out exitCode, out errorMessage))
        {
            return false;
        }

        return true;
    }

    public static string DeleteFile(string filePath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in the path
            filePath = Environment.ExpandEnvironmentVariables(filePath);

            // Check if file exists
            if (!File.Exists(filePath))
            {
                exitCode = 1;
                return "File not found: " + filePath;
            }

            // Normalize the path
            string normalizedPath = Path.GetFullPath(filePath);

            // Check if file is open in Visual Studio and close it
            EditorService.TryCloseFileInVisualStudio(normalizedPath);

            // Delete the file
            File.Delete(filePath);
            
            return "File deleted successfully: " + filePath;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error deleting file: " + ex.Message;
        }
    }

    public static string ListDirectory(string directoryPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in the path
            directoryPath = Environment.ExpandEnvironmentVariables(directoryPath);

            // Check if directory exists
            if (!Directory.Exists(directoryPath))
            {
                exitCode = 1;
                return "Directory not found: " + directoryPath;
            }

            StringBuilder result = new StringBuilder();
            result.AppendLine("Contents of: " + directoryPath);
            result.AppendLine();

            // List directories
            string[] directories = Directory.GetDirectories(directoryPath);
            if (directories.Length > 0)
            {
                result.AppendLine("Directories:");
                foreach (string dir in directories)
                {
                    string dirName = Path.GetFileName(dir);
                    result.AppendLine("  [DIR]  " + dirName);
                }
                result.AppendLine();
            }

            // List files
            string[] files = Directory.GetFiles(directoryPath);
            if (files.Length > 0)
            {
                result.AppendLine("Files:");
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    FileInfo fileInfo = new FileInfo(file);
                    long fileSizeBytes = fileInfo.Length;
                    string fileSize = FormatFileSize(fileSizeBytes);
                    result.AppendLine("  [FILE] " + fileName + " (" + fileSize + ")");
                }
            }

            if (directories.Length == 0 && files.Length == 0)
            {
                result.AppendLine("Directory is empty.");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error listing directory: " + ex.Message;
        }
    }

    // Helper method to ensure a directory exists
    public static bool EnsureDirectoryExists(string directoryPath, out int exitCode, out string errorMessage)
    {
        exitCode = 0;
        errorMessage = null;

        if (string.IsNullOrEmpty(directoryPath) || Directory.Exists(directoryPath))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            return true;
        }
        catch (Exception ex)
        {
            exitCode = 1;
            errorMessage = "Failed to create directory '" + directoryPath + "': " + ex.Message;
            return false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return string.Format("{0:0.##} {1}", len, sizes[order]);
    }
}
}

