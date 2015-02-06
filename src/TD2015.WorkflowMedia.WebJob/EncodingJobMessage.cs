using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TD2015.WorkflowMedia.WebJob
{
    public class EncodingJobMessage
    {
        public String MessageVersion { get; set; }

        public String EventType { get; set; }

        public String ETag { get; set; }

        public String TimeStamp { get; set; }

        public IDictionary<string, object> Properties { get; set; }
    }
}
