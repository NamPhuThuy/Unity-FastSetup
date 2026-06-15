# Developer Log - FastSetup Module

---

## [2026-06-15] FastUnsubmoduleTool: Robust Cleanup of Locked Submodule Files

### **Problem**
* **Root Cause**: **Locked DLLs** (e.g., `nice_vibrations_editor_plugin.dll` in `UP-Feel`) actively loaded in Unity's **AppDomain**.
* **Symptom**: `Directory.Delete(targetDir, true)` throws **`UnauthorizedAccessException`** ("Access to path is denied") on directory cleanup.
* **Result**: Incomplete deletion leaving corrupted submodule folder containing only locked DLLs.

### **Solution (Robust Deletion)**
1. **Clear Attributes**: Remove read-only flags.
2. **Direct Deletion**: Attempt standard `Directory.Delete`.
3. **Locked File Relocation** (Fallback):
   * Iterate contents.
   * If deletion fails (locked), **rename & move** file to `Temp/Deleteme` (project root).
   * *Rationale*: Windows allows moving/renaming locked DLLs within the same drive volume.
4. **Final Sweep**: Delete now-empty original folder structure.

### **Implementation**
```csharp
private static void SafeDeleteDirectory(string targetDir)
{
    if (!Directory.Exists(targetDir)) return;

    try
    {
        ClearAttributes(targetDir);
        Directory.Delete(targetDir, true);
        return;
    }
    catch (Exception ex)
    {
        LogWarning($"[FastUnsubmoduleTool] Initial deletion failed: {ex.Message}. Retrying robustly...");
    }

    string deletemeDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Deleteme");
    try { Directory.CreateDirectory(deletemeDir); } catch { }

    DeleteDirectoryContentsRobust(targetDir, deletemeDir);

    try
    {
        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
    }
    catch { }
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
        catch
        {
            try
            {
                string uniqueName = $"{Guid.NewGuid()}_{Path.GetFileName(file)}";
                File.Move(file, Path.Combine(deletemeDir, uniqueName));
            }
            catch { }
        }
    }

    foreach (string subDir in Directory.GetDirectories(srcDir))
    {
        DeleteDirectoryContentsRobust(subDir, deletemeDir);
        try { Directory.Delete(subDir, true); } catch { }
    }
}
```
