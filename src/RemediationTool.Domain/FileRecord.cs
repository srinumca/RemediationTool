using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemediationTool.Domain;

public class FileRecord
{
    public string FileName { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public string Status { get; set; } = "Active";
}
