using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage.Queue;
using System.Configuration;
using Microsoft.WindowsAzure.Storage.Blob;

namespace TD2015.WorkflowMedia.WebJob
{
    public class Functions
    {
        private static string _mediaServiceName = 
            ConfigurationManager.AppSettings["MediaServiceName"];

        private static string _mediaServiceKey = 
            ConfigurationManager.AppSettings["MediaServiceKey"];

        private static string _azureStorageAccount = 
            ConfigurationManager.ConnectionStrings["AzureWebJobsStorage"].ConnectionString;

        /// <summary>
        /// Methods triggered when a new blob is created in the "uploads" container
        /// </summary>
public static async Task ProcessNewBlobAsync(
    [BlobTrigger("uploads/{name}")] ICloudBlob cloudBlob,
    string name,
    TextWriter logger)
{
    await logger.WriteLineAsync(string.Format("New blob received: {0}", name));

    var mediaServiceWrapper = new MediaServiceWrapper(
        _mediaServiceName,
        _mediaServiceKey,
        _azureStorageAccount);

    // create the media service asset from the existing blob
    var asset = await mediaServiceWrapper
        .CreateMediaServiceAssetFromExistingBlobAsync(cloudBlob.Uri.ToString());

    // create a job with two tasks : multibirate mp4 generation and thumbnails generation
    var job = await mediaServiceWrapper
        .CreateJobAsync(asset, notificationEndpointsQueueName: "job-progress");

    await logger.WriteLineAsync(
        string.Format("The job has been submitted (job id = {0})", 
        job.Id));
}

        /// <summary>
        /// Methods triggered when a new message is push by Azure Media Services
        /// in the "job-progress" storage queue.
        /// </summary>
        public static async Task ProcessMediaServicesJobStateChanged(
            [QueueTrigger("job-progress")] CloudQueueMessage message, 
            TextWriter log)
        {
            var jobMessage = Newtonsoft.Json.JsonConvert.DeserializeObject<EncodingJobMessage>(message.AsString);

            //if the event type is a state change
            if (jobMessage.EventType == "JobStateChange")
            {
                //try get old and new state
                if (jobMessage.Properties.Any(p => p.Key == "OldState") 
                    && jobMessage.Properties.Any(p => p.Key == "NewState"))
                {
                    string oldJobState = jobMessage.Properties.First(p => p.Key == "OldState").Value.ToString();
                    string newJobState = jobMessage.Properties.First(p => p.Key == "NewState").Value.ToString();

                    await log.WriteLineAsync(string.Format("job state has changed from {0} to {1}", oldJobState, newJobState));

                    string newState = jobMessage.Properties["NewState"].ToString();
                    if (newState == "Finished")
                    {
                        string jobId = jobMessage.Properties["JobId"].ToString();

                        var mediaServiceWrapper = new MediaServiceWrapper(_mediaServiceName, _mediaServiceKey, _azureStorageAccount);
                        var result = await mediaServiceWrapper.PrepareAssetsForAdaptiveStreamingAsync(jobId);
                        await log.WriteLineAsync(string.Format("Smooth Streaming available at {0}", result.SmoothStreamingUrl));
                    }
                }
            }
        }
    }
}
