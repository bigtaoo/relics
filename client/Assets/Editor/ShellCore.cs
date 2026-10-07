using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Automatic.Editor
{
    /// <summary>
    /// Battle.Core is compiled into the shell (design/10 §4, ADR-010), so a hot update cannot change
    /// it: the player would keep running the shell's copy while the hot code expects the new one.
    /// A player build records a hash of the core sources; a hot update refuses to publish when the
    /// sources differ. Like CheckShellApi it assumes the last local player build of the target is
    /// the shell on the CDN.
    /// </summary>
    public static class ShellCore
    {
        public static void Record(BuildTarget target)
        {
            var file = RecordFile(target);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, SourceHash());
        }

        public static void Check(BuildTarget target)
        {
            var file = RecordFile(target);
            if (!File.Exists(file))
                throw new FileNotFoundException($"{file} missing: run 'Automatic/3. Build Player' first.");
            if (File.ReadAllText(file).Trim() != SourceHash())
                throw new InvalidOperationException(
                    "src/Battle.Core changed since the last player build: the core is part of the shell and " +
                    "cannot be hot updated. Ship a new shell ('Automatic/3. Build Player'), or revert the core.");
        }

        /// <summary>Over the .cs files by path, with line endings normalized: checkouts may differ in them.</summary>
        public static string SourceHash()
        {
            var root = Path.Combine(HotUpdateBuild.RepoRoot, "src", "Battle.Core");
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(f => !f.StartsWith("bin/") && !f.StartsWith("obj/"))
                .OrderBy(f => f, StringComparer.Ordinal);
            using var sha = SHA256.Create();
            var text = new StringBuilder();
            foreach (var f in files)
                text.Append(f).Append('\n').Append(File.ReadAllText(Path.Combine(root, f)).Replace("\r\n", "\n")).Append('\0');
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static string RecordFile(BuildTarget target) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../HybridCLRData/ShellCore", HotUpdateBuild.PlatformFolder(target) + ".txt"));
    }
}
