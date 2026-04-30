using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemediationTool.Application.Interfaces
{
    public interface IStorageService
    {
        Task UploadAsync(string key, Stream data);
        Task<Stream> DownloadAsync(string key);
        Task MoveAsync(string sourceKey, string destinationKey);
        Task DeleteAsync(string key);
    }
}
