
using RemediationTool.Application.Interfaces;
using System;
using System.IO;

namespace RemediationTool.Infrastructure.FileServices;

// Handles physical file operations
public class FileService : IFileService
{
    public string Quarantine(string sourcePath,string quarantineRoot)
    {
        try
        {
            if(!Directory.Exists(quarantineRoot))
                Directory.CreateDirectory(quarantineRoot);

            var name = Path.GetFileName(sourcePath);
            var dest = Path.Combine(quarantineRoot,name);

            File.Copy(sourcePath,dest,true);
            File.Delete(sourcePath);

            return dest;
        }
        catch (Exception)
        {
            // rethrow so caller can log or handle
            throw;
        }
    }

    public void CreateStub(string originalPath)
    {
        try
        {
            var stub = originalPath + "_Retention_Placeholder";
            File.WriteAllText(stub,"File moved due to retention policy.");
        }
        catch (Exception)
        {
            // let caller decide how to handle
            throw;
        }
    }
}
