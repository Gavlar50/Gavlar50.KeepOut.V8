using System.Collections.Generic;

namespace Gavlar50.KeepOut.Models
{
    public class KeepOutRule
    {
        public int NoAccessPage { get; set; }
        public int PageToSecure { get; set; }
        public List<string> DeniedMemberGroups { get; set; }
        public string CoverageColour { get; set; }
    }
}