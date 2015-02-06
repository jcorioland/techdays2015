using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;

namespace TD2015.WorkflowMedia.WebJob
{
    class Program
    {
        static void Main()
        {
            var configuration = new JobHostConfiguration();

            // check the queue all five seconds
            configuration.Queues.MaxPollingInterval = TimeSpan.FromSeconds(5);

            // process a message five times max, after put it in the poisoning queue
            configuration.Queues.MaxDequeueCount = 5;

            // max 5 messages in parallel per webjob instance
            configuration.Queues.BatchSize = 5;

            var host = new JobHost(configuration);

            // run the webjob host
            host.RunAndBlock();
        }
    }
}
