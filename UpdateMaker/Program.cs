using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace UpdateMaker
{
    class Program
    {
        static string GetRelativePath(string basePath, string targetPath)
        {
            if (!targetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Target path is not within base path.");
            }

            string relativePath = targetPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return relativePath.Replace('\\', '/');
        }

        static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string sourceDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\DgzAIO\bin\Release"));
            string zipPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dgz.zip");
            string jsonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "update_agent_info.json");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".exe") || f.EndsWith(".dll") || f.EndsWith(".exe.config"))
                .ToList();

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    string relativePath = GetRelativePath(sourceDir, file);  // Nisbiy yo'lni olish (Release olib tashlanadi)

                    zip.CreateEntryFromFile(file, relativePath);
                }

                foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                {
                    string relativeDir = GetRelativePath(sourceDir, dir);
                    zip.CreateEntry(relativeDir + "/"); // Papkani ZIPga qo'shish
                }
            }

            string exePath = Path.Combine(sourceDir, "DgzAIO.exe");
            string version = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath).FileVersion;

            var updateInfo = new
            {
                version = version,
                file_name = Path.GetFileName(zipPath)
            };

            string json = JsonConvert.SerializeObject(updateInfo, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(jsonPath, json);

            Console.WriteLine("ZIP fayl yaratildi: " + zipPath);
            Console.WriteLine("JSON fayl yaratildi: " + jsonPath);
        }
    }
}
