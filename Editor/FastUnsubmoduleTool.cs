using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Techies
{
    public static class FastUnsubmoduleTool
    {
        #region Data Structures

        public class SubmoduleInfo
        {
            public string Name;
            public string Path;
            public string Url;
        }

        #endregion

        #region Menu Items

        [MenuItem("Assets/NamPhuThuy/FastSetup/Un-submodule from file", true)]
        private static bool ValidateUnsubmoduleFromFile()
        {
            var activeObject = Selection.activeObject;
            if (activeObject == null) return false;

            string assetPath = AssetDatabase.GetAssetPath(activeObject);
            if (string.IsNullOrEmpty(assetPath)) return false;

            return assetPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/NamPhuThuy/FastSetup/Un-submodule from file")]
        private static void UnsubmoduleFromFile()
        {
            InitializeLogFile();
            LogInfo("[FastUnsubmoduleTool] UnsubmoduleFromFile triggered.");

            string projectPath = Directory.GetCurrentDirectory();
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string fullPath = Path.Combine(projectPath, assetPath);

            if (!File.Exists(fullPath))
            {
                LogError("File not found: " + fullPath);
                return;
            }

            LogInfo($"[FastUnsubmoduleTool] Reading configuration file: {fullPath}");
            string[] lines = File.ReadAllLines(fullPath);
            var tasks = new List<(SubmoduleInfo sub, string destPath)>();
            var submodules = LoadSubmodules();

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                string srcPath = trimmed;
                string destPath = "";

                if (trimmed.Contains("->"))
                {
                    var parts = trimmed.Split(new[] { "->" }, StringSplitOptions.None);
                    srcPath = parts[0].Trim();
                    destPath = parts[1].Trim();
                }

                // Standardize paths
                srcPath = srcPath.Replace('\\', '/');
                destPath = destPath.Replace('\\', '/');

                var sub = submodules.FirstOrDefault(s => s.Path.Equals(srcPath, StringComparison.OrdinalIgnoreCase));
                if (sub == null)
                {
                    LogWarning($"[FastUnsubmoduleTool] Submodule not found in .gitmodules for path: {srcPath}");
                    continue;
                }

                if (string.IsNullOrEmpty(destPath))
                {
                    string parentDir = Path.GetDirectoryName(sub.Path).Replace('\\', '/');
                    string folderName = Path.GetFileName(sub.Path);
                    string suggestedNewName = folderName.Replace('-', '_');
                    destPath = string.IsNullOrEmpty(parentDir) ? suggestedNewName : $"{parentDir}/{suggestedNewName}";
                }

                tasks.Add((sub, destPath));
            }

            if (tasks.Count == 0)
            {
                LogWarning("[FastUnsubmoduleTool] No valid submodules listed in the file were found.");
                EditorUtility.DisplayDialog("No Submodules Found", "No valid submodules listed in the file were found.", "OK");
                return;
            }

            // Confirm before processing
            string confirmMsg = $"Are you sure you want to un-submodule the following {tasks.Count} submodule(s)?\n\n" +
                                string.Join("\n", tasks.Select(t => $"- {Path.GetFileName(t.sub.Path)} -> {Path.GetFileName(t.destPath)}"));

            if (!EditorUtility.DisplayDialog("Confirm Un-submodule & Merge", confirmMsg, "Yes, Process", "Cancel"))
            {
                LogInfo("[FastUnsubmoduleTool] Process cancelled by user.");
                return;
            }

            LogInfo($"[FastUnsubmoduleTool] Starting un-submoduling process for {tasks.Count} submodules.");
            int successCount = 0;
            int total = tasks.Count;

            for (int i = 0; i < total; i++)
            {
                var task = tasks[i];
                float progress = (float)i / total;
                EditorUtility.DisplayProgressBar("Un-submodule Tool", $"Processing {task.sub.Name} ({i + 1}/{total})...", progress);

                try
                {
                    ProcessUnsubmoduleSilent(task.sub, task.destPath);
                    successCount++;
                }
                catch (Exception ex)
                {
                    LogError($"[FastUnsubmoduleTool] Failed to un-submodule {task.sub.Name}: {ex.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            LogInfo($"[FastUnsubmoduleTool] Finished processing {successCount}/{total} submodules successfully.");
            EditorUtility.DisplayDialog("Finished", $"Successfully un-submoduled {successCount}/{total} submodules.", "OK");
        }

        [MenuItem("Assets/NamPhuThuy/FastSetup/SearchAllSubmodules", true)]
        private static bool ValidateSearchAllSubmodules()
        {
            var activeObject = Selection.activeObject;
            if (activeObject == null) return false;

            string assetPath = AssetDatabase.GetAssetPath(activeObject);
            if (string.IsNullOrEmpty(assetPath)) return false;

            return assetPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/NamPhuThuy/FastSetup/SearchAllSubmodules")]
        private static void SearchAllSubmodules()
        {
            InitializeLogFile();
            LogInfo("[FastUnsubmoduleTool] SearchAllSubmodules triggered.");

            var activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                LogWarning("[FastUnsubmoduleTool] No active object selected.");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(activeObject);
            string projectPath = Directory.GetCurrentDirectory();
            string fullPath = Path.Combine(projectPath, assetPath);

            if (!File.Exists(fullPath))
            {
                LogError("File not found: " + fullPath);
                return;
            }

            var submodules = LoadSubmodules();
            if (submodules.Count == 0)
            {
                LogWarning("[FastUnsubmoduleTool] No submodules found in .gitmodules.");
                EditorUtility.DisplayDialog("Search Submodules", "No submodules found in .gitmodules.", "OK");
                return;
            }

            // Sort submodules alphabetically by Path
            var sortedSubmodules = submodules.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToList();

            var lines = new List<string>();
            lines.Add("# List of submodules found in the project");
            lines.Add("# Right-click this file and select Assets -> FastSetup -> Un-submodule from file to convert them.");
            lines.Add("");

            for (int i = 0; i < sortedSubmodules.Count; i++)
            {
                var sub = sortedSubmodules[i];
                string parentDir = Path.GetDirectoryName(sub.Path).Replace('\\', '/');
                string folderName = Path.GetFileName(sub.Path);
                string newFolderName = folderName.Replace('-', '_');
                string destPath = string.IsNullOrEmpty(parentDir) ? newFolderName : $"{parentDir}/{newFolderName}";

                lines.Add($"{sub.Path} -> {destPath}");
                lines.Add("");
            }

            LogInfo($"[FastUnsubmoduleTool] Writing {submodules.Count} submodules to: {fullPath}");
            File.WriteAllLines(fullPath, lines);
            AssetDatabase.ImportAsset(assetPath);

            LogInfo($"[FastUnsubmoduleTool] Search completed. Wrote {submodules.Count} submodules to {Path.GetFileName(assetPath)}.");
            EditorUtility.DisplayDialog("Success", $"Wrote {submodules.Count} submodules to {Path.GetFileName(assetPath)}.", "OK");
        }

        #endregion

        #region Submodule Processing

        private static void ProcessUnsubmoduleSilent(SubmoduleInfo sub, string destPath)
        {
            ProcessUnsubmoduleInternal(sub, destPath, false);
        }

        private static void ProcessUnsubmoduleInternal(SubmoduleInfo sub, string destPath, bool showProgress)
        {
            LogInfo($"[FastUnsubmoduleTool] Processing submodule: '{sub.Name}'");
            LogInfo($"[FastUnsubmoduleTool] Source Path: '{sub.Path}'");
            LogInfo($"[FastUnsubmoduleTool] Destination Path: '{destPath}'");

            string srcPath = sub.Path;
            string srcFullPath = Path.Combine(Directory.GetCurrentDirectory(), srcPath);
            string destFullPath = Path.Combine(Directory.GetCurrentDirectory(), destPath);

            // Step 1: Copy content
            LogInfo($"[FastUnsubmoduleTool] Step 1/5: Copying contents from '{srcFullPath}' to '{destFullPath}'");
            if (showProgress) EditorUtility.DisplayProgressBar("Un-submodule Tool", "Step 1/5: Copying contents...", 0.2f);
            CopyDirectory(srcFullPath, destFullPath);

            // Step 2: Git deinit
            LogInfo($"[FastUnsubmoduleTool] Step 2/5: De-initializing Git submodule '{srcPath}'");
            if (showProgress) EditorUtility.DisplayProgressBar("Un-submodule Tool", "Step 2/5: De-initializing Git submodule...", 0.4f);
            RunGitCommand($"submodule deinit -f \"{srcPath}\"");

            // Step 3: Git rm cached
            LogInfo($"[FastUnsubmoduleTool] Step 3/5: Removing submodule '{srcPath}' from index");
            if (showProgress) EditorUtility.DisplayProgressBar("Un-submodule Tool", "Step 3/5: Removing submodule from index...", 0.6f);
            RunGitCommand($"rm -f --cached \"{srcPath}\"");

            // Step 4: Clean gitmodules & config
            LogInfo($"[FastUnsubmoduleTool] Step 4/5: Cleaning Git modules cache for '{sub.Name}'");
            if (showProgress) EditorUtility.DisplayProgressBar("Un-submodule Tool", "Step 4/5: Cleaning Git modules cache...", 0.8f);
            RemoveSubmoduleFromGitmodules(sub.Name);

            // Stage the changes to .gitmodules so subsequent git commands in a batch don't fail with dirty index errors
            try
            {
                LogInfo("[FastUnsubmoduleTool] Staging changes to .gitmodules");
                RunGitCommand("add .gitmodules");
            }
            catch (Exception ex)
            {
                LogWarning($"[FastUnsubmoduleTool] Failed to stage .gitmodules: {ex.Message}");
            }

            string gitModulesPath = Path.Combine(Directory.GetCurrentDirectory(), ".git", "modules", srcPath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(gitModulesPath))
            {
                LogInfo($"[FastUnsubmoduleTool] Deleting git modules folder: '{gitModulesPath}'");
                SafeDeleteDirectory(gitModulesPath);
            }

            // Step 5: Delete original submodule folder
            LogInfo($"[FastUnsubmoduleTool] Step 5/5: Deleting old submodule folder '{srcFullPath}'");
            if (showProgress) EditorUtility.DisplayProgressBar("Un-submodule Tool", "Step 5/5: Deleting old folder...", 0.9f);
            SafeDeleteDirectory(srcFullPath);
            
            string metaPath = srcFullPath + ".meta";
            if (File.Exists(metaPath))
            {
                LogInfo($"[FastUnsubmoduleTool] Deleting meta file: '{metaPath}'");
                File.Delete(metaPath);
            }

            if (showProgress) EditorUtility.ClearProgressBar();
            LogInfo($"[FastUnsubmoduleTool] Successfully completed un-submoduling '{sub.Name}'");
        }

        #endregion

        #region Git / File Management

        private static List<SubmoduleInfo> LoadSubmodules()
        {
            var list = new List<SubmoduleInfo>();
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), ".gitmodules");
            if (!File.Exists(filePath)) return list;

            string[] lines = File.ReadAllLines(filePath);
            SubmoduleInfo current = null;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[submodule"))
                {
                    current = new SubmoduleInfo();
                    int start = trimmed.IndexOf('"');
                    int end = trimmed.LastIndexOf('"');
                    if (start >= 0 && end > start)
                    {
                        current.Name = trimmed.Substring(start + 1, end - start - 1);
                    }
                    list.Add(current);
                }
                else if (current != null)
                {
                    if (trimmed.StartsWith("path ="))
                    {
                        current.Path = trimmed.Substring("path =".Length).Trim().Replace('\\', '/');
                    }
                    else if (trimmed.StartsWith("url ="))
                    {
                        current.Url = trimmed.Substring("url =".Length).Trim();
                    }
                }
            }
            return list;
        }

        private static void RemoveSubmoduleFromGitmodules(string submoduleName)
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), ".gitmodules");
            if (!File.Exists(filePath)) return;

            string[] lines = File.ReadAllLines(filePath);
            var newLines = new List<string>();
            bool skipMode = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[submodule"))
                {
                    int start = trimmed.IndexOf('"');
                    int end = trimmed.LastIndexOf('"');
                    if (start >= 0 && end > start)
                    {
                        string name = trimmed.Substring(start + 1, end - start - 1);
                        if (name.Equals(submoduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            skipMode = true;
                            continue;
                        }
                    }
                    skipMode = false;
                }
                else if (skipMode)
                {
                    if (trimmed.StartsWith("path =") || trimmed.StartsWith("url =") || string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }
                    skipMode = false;
                }

                newLines.Add(line);
            }

            File.WriteAllLines(filePath, newLines);
        }

        private static string RunGitCommand(string arguments)
        {
            LogInfo($"[FastUnsubmoduleTool] Executing command: git {arguments}");
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                process.WaitForExit();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                
                if (!string.IsNullOrEmpty(output))
                {
                    LogInfo($"[FastUnsubmoduleTool] Command output:\n{output.Trim()}");
                }
                
                if (process.ExitCode != 0)
                {
                    string gitError = $"Git command failed: git {arguments}\nError: {error}";
                    LogError($"[FastUnsubmoduleTool] {gitError}");
                    throw new Exception(gitError);
                }
                return output;
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destSubDir = Path.Combine(destDir, dirName);
                CopyDirectory(subDir, destSubDir);
            }
        }

        private static void SafeDeleteDirectory(string targetDir)
        {
            if (!Directory.Exists(targetDir)) return;

            // First, try normal deletion after clearing attributes
            try
            {
                ClearAttributes(targetDir);
                Directory.Delete(targetDir, true);
                return;
            }
            catch (Exception ex)
            {
                LogWarning($"[FastUnsubmoduleTool] Initial directory deletion of '{targetDir}' failed: {ex.Message}. Retrying with robust cleanup...");
            }

            // If it failed, delete file by file, and move locked files to a temporary folder
            string deletemeDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Deleteme");
            try
            {
                if (!Directory.Exists(deletemeDir))
                {
                    Directory.CreateDirectory(deletemeDir);
                }
            }
            catch (Exception ex)
            {
                LogError($"[FastUnsubmoduleTool] Failed to create Deleteme directory: {ex.Message}");
            }

            DeleteDirectoryContentsRobust(targetDir, deletemeDir);

            // Finally, try to delete the directory again
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }
            }
            catch (Exception ex)
            {
                LogWarning($"[FastUnsubmoduleTool] Could not delete directory '{targetDir}' even after robust cleanup: {ex.Message}");
            }
        }

        private static void ClearAttributes(string targetDir)
        {
            string[] files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch { }
            }

            string[] dirs = Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories);
            foreach (string dir in dirs)
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    di.Attributes = FileAttributes.Normal;
                }
                catch { }
            }
        }

        private static void DeleteDirectoryContentsRobust(string srcDir, string deletemeDir)
        {
            foreach (string file in Directory.GetFiles(srcDir))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    LogWarning($"[FastUnsubmoduleTool] Failed to delete file '{file}': {ex.Message}. Attempting to move it to temp...");
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        string uniqueName = $"{Guid.NewGuid()}_{fileName}";
                        string destFile = Path.Combine(deletemeDir, uniqueName);
                        File.Move(file, destFile);
                        LogInfo($"[FastUnsubmoduleTool] Successfully moved locked file to '{destFile}'");
                    }
                    catch (Exception moveEx)
                    {
                        LogError($"[FastUnsubmoduleTool] Failed to move locked file '{file}': {moveEx.Message}");
                    }
                }
            }

            foreach (string subDir in Directory.GetDirectories(srcDir))
            {
                DeleteDirectoryContentsRobust(subDir, deletemeDir);
                try
                {
                    Directory.Delete(subDir, true);
                }
                catch { }
            }
        }

        #endregion

        #region Logging

        private static string LogFilePath => Path.Combine(Directory.GetCurrentDirectory(), "Assets/_Project/Module EditorTools/UP-FastSetup/Sample/Unmodule_log.text");

        private static void InitializeLogFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --- FastUnsubmoduleTool Log Started ---\n");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FastUnsubmoduleTool] Failed to initialize log file: {ex.Message}");
            }
        }

        private static void LogInfo(string message)
        {
            Debug.Log(message);
            AppendToLogFile($"[INFO] {message}");
        }

        private static void LogWarning(string message)
        {
            Debug.LogWarning(message);
            AppendToLogFile($"[WARNING] {message}");
        }

        private static void LogError(string message)
        {
            Debug.LogError(message);
            AppendToLogFile($"[ERROR] {message}");
        }

        private static void AppendToLogFile(string message)
        {
            try
            {
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FastUnsubmoduleTool] Failed to write to log file: {ex.Message}");
            }
        }

        #endregion
    }
}
