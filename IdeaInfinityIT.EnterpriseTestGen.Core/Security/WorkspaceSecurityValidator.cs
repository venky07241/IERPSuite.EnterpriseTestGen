using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;

namespace IdeaInfinityIT.EnterpriseTestGen.Core.Security
{
    public static class WorkspaceSecurityValidator
    {
        private static readonly string[] ForbiddenPrefixes = GetForbiddenPrefixes();

        private static string[] GetForbiddenPrefixes()
        {
            var list = new List<string>();
            try
            {
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(win)) list.Add(Path.GetFullPath(win));

                var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrEmpty(sys)) list.Add(Path.GetFullPath(sys));

                var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(prog)) list.Add(Path.GetFullPath(prog));

                var prog86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(prog86)) list.Add(Path.GetFullPath(prog86));
            }
            catch { }

            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string ValidateAndCanonicalizePath(string inputPath, bool mustExist = true)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("Path cannot be empty or whitespace.", nameof(inputPath));

            string fullPath = Path.GetFullPath(inputPath);

            // Disallow root drives alone as work directory (e.g. C:\)
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Access to the drive root '{fullPath}' is restricted. Please specify a specific project or workspace folder.");
            }

            // Check against protected system directories
            foreach (var forbidden in ForbiddenPrefixes)
            {
                if (fullPath.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Access to system folder '{fullPath}' is prohibited for security reasons.");
                }
            }

            if (mustExist && !Directory.Exists(fullPath) && !File.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Target path does not exist on disk: '{fullPath}'");
            }

            return fullPath;
        }

        public static string ValidateRelativePath(string baseDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Relative path cannot be empty.", nameof(relativePath));

            string canonicalBase = Path.GetFullPath(baseDirectory);
            string cleanRel = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string targetPath = Path.GetFullPath(Path.Combine(canonicalBase, cleanRel));

            string baseWithSep = canonicalBase.EndsWith(Path.DirectorySeparatorChar.ToString()) 
                ? canonicalBase 
                : canonicalBase + Path.DirectorySeparatorChar;

            if (!targetPath.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Directory traversal attempt detected in path: '{relativePath}'.");
            }

            return targetPath;
        }

        public static void SafeExtractZip(string zipFilePath, string destinationDirectory, long maxExtractionBytes = 2L * 1024 * 1024 * 1024, int maxEntries = 100000)
        {
            if (!File.Exists(zipFilePath))
                throw new FileNotFoundException($"ZIP file not found: {zipFilePath}");

            string canonicalDest = Path.GetFullPath(destinationDirectory);
            if (!Directory.Exists(canonicalDest))
            {
                Directory.CreateDirectory(canonicalDest);
            }

            string destWithSep = canonicalDest.EndsWith(Path.DirectorySeparatorChar.ToString()) 
                ? canonicalDest 
                : canonicalDest + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(zipFilePath);
            if (archive.Entries.Count > maxEntries)
            {
                throw new SecurityException($"Archive contains {archive.Entries.Count} entries, exceeding maximum safety limit of {maxEntries}.");
            }

            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                totalBytes += entry.Length;
                if (totalBytes > maxExtractionBytes)
                {
                    throw new SecurityException($"Total uncompressed size exceeds maximum safety limit of {maxExtractionBytes / (1024 * 1024)}MB (possible decompression bomb).");
                }

                string entryDest = Path.GetFullPath(Path.Combine(canonicalDest, entry.FullName));
                if (!entryDest.StartsWith(destWithSep, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Zip-Slip path traversal vulnerability detected in archive entry: '{entry.FullName}'.");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Directory entry
                    Directory.CreateDirectory(entryDest);
                }
                else
                {
                    // File entry
                    var dir = Path.GetDirectoryName(entryDest);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    entry.ExtractToFile(entryDest, overwrite: true);
                }
            }
        }

        public static void ValidateTestSavePath(string filePath)
        {
            var canonical = ValidateAndCanonicalizePath(Path.GetDirectoryName(filePath) ?? string.Empty, mustExist: false);
            var ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Saving file with extension '{ext}' is forbidden. Only C# test source files (.cs) are allowed.");
            }
        }
    }
}
