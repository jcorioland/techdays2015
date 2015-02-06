using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TD2015.WorkflowMedia.WebJob
{
    public class MediaServiceWrapper
    {
        private readonly CloudMediaContext _cloudMediaContext;
        private readonly CloudStorageAccount _cloudStorageAccount;

        public MediaServiceWrapper(string mediaServiceName, string mediaServiceKey, string storageConnectionString)
        {
            if (string.IsNullOrEmpty(mediaServiceName))
            {
                throw new ArgumentNullException("mediaServiceName");
            }

            if (string.IsNullOrEmpty(mediaServiceKey))
            {
                throw new ArgumentNullException("mediaServiceKey");
            }

            if (string.IsNullOrEmpty(storageConnectionString))
            {
                throw new ArgumentNullException("storageConnectionString");
            }

            _cloudMediaContext = new CloudMediaContext(mediaServiceName, mediaServiceKey);
            _cloudStorageAccount = CloudStorageAccount.Parse(storageConnectionString);
        }

        public async Task<IAsset> CreateMediaServiceAssetFromExistingBlobAsync(string blobUrl)
        {
            var cloudBlobClient = _cloudStorageAccount.CreateCloudBlobClient();

            // get a valid reference on the source blob
            var sourceBlob = await cloudBlobClient.GetBlobReferenceFromServerAsync(new Uri(blobUrl));
            if(sourceBlob.BlobType != Microsoft.WindowsAzure.Storage.Blob.BlobType.BlockBlob)
            {
                throw new ArgumentException("Azure Media Services only works with block blobs.");
            }

            // create a new media service asset
            var asset = await _cloudMediaContext.Assets.CreateAsync(sourceBlob.Name, AssetCreationOptions.None, CancellationToken.None);

            // create a write policy valid for one hour
            var writePolicy = await _cloudMediaContext.AccessPolicies.CreateAsync(
                "Write policy",
                TimeSpan.FromHours(1),
                AccessPermissions.Write);

            // get a locator to be able to copy the blob in the media service asset location
            var locator = await _cloudMediaContext.Locators.CreateSasLocatorAsync(asset, writePolicy);

            // get the container name & reference
            string destinationContainerName = new Uri(locator.Path).Segments[1];
            var destinationContainer = cloudBlobClient.GetContainerReference(destinationContainerName);

            // create the container if it does not exist
            if ((await destinationContainer.CreateIfNotExistsAsync()))
            {
                destinationContainer.SetPermissions(new Microsoft.WindowsAzure.Storage.Blob.BlobContainerPermissions()
                {
                    PublicAccess = Microsoft.WindowsAzure.Storage.Blob.BlobContainerPublicAccessType.Blob
                });
            }

            // create the asset file
            var assetFile = await asset.AssetFiles.CreateAsync(sourceBlob.Name, CancellationToken.None);

            // copie the blob
            var destinationBlob = destinationContainer.GetBlockBlobReference(sourceBlob.Name);
            await destinationBlob.StartCopyFromBlobAsync(sourceBlob as CloudBlockBlob);

            // fetch the blob attributes
            await destinationBlob.FetchAttributesAsync();

            if(destinationBlob.Properties.Length != sourceBlob.Properties.Length)
            {
                throw new InvalidOperationException("The blob was not well copied");
            }

            // remove old stuff
            await locator.DeleteAsync();
            await writePolicy.DeleteAsync();
            await sourceBlob.DeleteAsync();

            await asset.UpdateAsync();
            return asset;
        }

        public async Task<IJob> CreateMultibitrateGenerationJobAsync(IAsset mediaServiceAsset, string notificationEndpointsQueueName)
        {
            // create the job
            string jobName = string.Format("Multibitrate generation for {0}", mediaServiceAsset.Name);
            var job = _cloudMediaContext.Jobs.Create(jobName);

            // get the azure media encoder
            var azureMediaEncoder = GetLatestMediaProcessorByName("Azure Media Encoder");

            // add a task for thumbnail generation
            var thumbnailTask = job.Tasks.AddNew("Multibitrate", azureMediaEncoder, "H264 Adaptive Bitrate MP4 Set 720p", TaskOptions.None);
            thumbnailTask.InputAssets.Add(mediaServiceAsset);
            thumbnailTask.OutputAssets.AddNew(string.Format("Multibirate ouput for {0}", mediaServiceAsset.Name), AssetCreationOptions.None);

            // create a notification endpoint
            await EnsureQueueExistsAsync(notificationEndpointsQueueName);
            var notificationEndPoint = await _cloudMediaContext.NotificationEndPoints
                .CreateAsync(
                    "notification",
                    NotificationEndPointType.AzureQueue,
                    notificationEndpointsQueueName
                );

            job.JobNotificationSubscriptions.AddNew(NotificationJobState.All, notificationEndPoint);

            // submit the job
            job = await job.SubmitAsync();

            return job;
        }

        public async Task<AdaptiveStreamingInfo> PrepareAssetsForAdaptiveStreamingAsync(string jobId)
        {
            // get the job from the cloud media context
            var theJob = _cloudMediaContext.Jobs
                .Where(j => j.Id == jobId)
                .AsEnumerable()
                .FirstOrDefault();

            if (theJob == null)
            {
                throw new InvalidOperationException("The job is not finished");
            }

            var adaptiveStreamingInfo = new AdaptiveStreamingInfo();

            // assets publication
            foreach (var outputAsset in theJob.OutputMediaAssets)
            {
                // multi-bitrate MP4
                if (outputAsset.IsStreamable)
                {
                    var streamingLocator = await GetStreamingLocatorForAssetAsync(outputAsset);
                    adaptiveStreamingInfo.SmoothStreamingUrl = streamingLocator.GetSmoothStreamingUri().ToString();
                    adaptiveStreamingInfo.HlsUrl = streamingLocator.GetHlsUri().ToString();
                    adaptiveStreamingInfo.MpegDashUrl = streamingLocator.GetMpegDashUri().ToString();
                }
                // thumbnails
                else
                {
                    var locator = await GetSasLocatorForAssetAsync(outputAsset);

                    var posterFiles = outputAsset.AssetFiles
                        .Where(f => f.Name.EndsWith(".jpg"))
                        .AsEnumerable();

                    foreach (var posterFile in posterFiles)
                    {
                        string posterUrl = string.Format("{0}/{1}{2}", locator.BaseUri, posterFile.Name, locator.ContentAccessComponent);
                        adaptiveStreamingInfo.Posters.Add(posterUrl);
                    }
                }
            }

            return adaptiveStreamingInfo;
        }

        /// <summary>
        /// Gets the streaming locator for an asset
        /// </summary>
        /// <param name="asset">The asset</param>
        /// <returns>The locator, wrapped in a Task, for asynchronous execution</returns>
        private async Task<ILocator> GetStreamingLocatorForAssetAsync(IAsset asset)
        {
            // the asset should be streamable
            if (!asset.IsStreamable)
            {
                throw new InvalidOperationException("This asset cannot be streamed.");
            }

            // get the locator on the asset
            var locator = asset.Locators
                .Where(l => l.Name == "vwr_streaming_locator")
                .FirstOrDefault();

            // if it does not exist
            if (locator == null)
            {
                // get the access policy
                var accessPolicy = await _cloudMediaContext
                        .AccessPolicies
                        .CreateAsync("vwr_streaming_access_policy", TimeSpan.FromDays(100 * 365), AccessPermissions.Read);

                // create the locator on the asset
                locator = await _cloudMediaContext
                    .Locators
                    .CreateLocatorAsync(
                        LocatorType.OnDemandOrigin,
                        asset,
                        accessPolicy,
                        DateTime.UtcNow.AddMinutes(-5),
                        name: "vwr_streaming_locator");
            }

            // returns the locator
            return locator;
        }

        /// <summary>
        /// Gets a SAS locator for a given asset
        /// </summary>
        /// <param name="asset">The asset</param>
        /// <returns>The locator, wrapped in a Task for asynchronous execution</returns>
        private async Task<ILocator> GetSasLocatorForAssetAsync(IAsset asset)
        {
            string locatorName = string.Format("SasLocator for {0}", asset.Id);
            var locator = _cloudMediaContext.Locators
                .Where(l => l.Name == locatorName)
                .FirstOrDefault();

            if (locator == null)
            {
                // create the access policy
                var accessPolicy = await _cloudMediaContext
                        .AccessPolicies
                        .CreateAsync(string.Format("SasPolicy for {0}", asset.Id), TimeSpan.FromDays(100 * 365), AccessPermissions.Read);

                // create the locator on the asset
                locator = await _cloudMediaContext
                    .Locators
                    .CreateLocatorAsync(
                        LocatorType.Sas,
                        asset,
                        accessPolicy,
                        DateTime.UtcNow.AddMinutes(-10),
                        name: locatorName);
            }

            // returns the locator
            return locator;
        }

        private async Task EnsureQueueExistsAsync(string notificationEndpointsQueueName)
        {
            //create the cloud queue client from the storage account
            CloudQueueClient cloudQueueClient = _cloudStorageAccount.CreateCloudQueueClient();

            //get a cloud queue reference
            CloudQueue notificationsQueue = cloudQueueClient.GetQueueReference(notificationEndpointsQueueName);

            //create the queue if it does not exist
            await notificationsQueue.CreateIfNotExistsAsync();
        }

        private IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = _cloudMediaContext.MediaProcessors.Where(p => p.Name == mediaProcessorName).
                ToList().OrderBy(p => new Version(p.Version)).LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor", mediaProcessorName));

            return processor;
        }
    }
}
