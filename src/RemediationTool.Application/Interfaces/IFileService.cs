using System;

namespace RemediationTool.Application.Interfaces
{
    public interface IFileService
    {
        string Quarantine(string sourcePath, string quarantineRoot);
        void CreateStub(string originalPath);
    }
}
