using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.Data
{
    public class AllLibsSummary
    {
        public List<LibSummary>? libSummaries { get; set; }
        public AllLibsSummary()
        { 
            libSummaries = new List<LibSummary>();
        }
    }

    public class LibSummary
    {
        public int LibID { get; set; }
        public string Floor { get; set; }
        public string Name { get; set; }
        public bool IsOpen { get; set; }
    }
}
