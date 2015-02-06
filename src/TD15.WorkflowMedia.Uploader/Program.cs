using Microsoft.WindowsAzure.Storage;
using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace TD15.WorkflowMedia.Uploader
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Chemin du fichier à envoyer:");
            string filePath = Console.ReadLine();

            Console.WriteLine("Envoi du fichier en cours...");

            Uri blobUri = UploadFileAsync(filePath).Result;

            Console.WriteLine("Le fichier a été envoyé dans Azure: {0}", blobUri);

            PushToUploadQueueAsync(blobUri).Wait();

            Console.WriteLine("OK !");
        }

        static async Task PushToUploadQueueAsync(Uri blobUri)
        {
            string azureStorageConnectionString =
                ConfigurationManager.ConnectionStrings["AzureStorage"].ConnectionString;

            // création d'un queue client
            var cloudStorageAccount = CloudStorageAccount.Parse(azureStorageConnectionString);
            var cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();

            // récupération de la queue
            var uploadQueue = cloudQueueClient.GetQueueReference("upload");
            await uploadQueue.CreateIfNotExistsAsync();

            // envoie d'un message qui contient l'url du blob
            await uploadQueue.AddMessageAsync(new Microsoft.WindowsAzure.Storage.Queue.CloudQueueMessage(blobUri.ToString()));
        }

        static async Task<Uri> UploadFileAsync(string filePath)
        {
            string azureStorageConnectionString =
                ConfigurationManager.ConnectionStrings["AzureStorage"].ConnectionString;

            var cloudStorageAccount = CloudStorageAccount.Parse(azureStorageConnectionString);
            var cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();

            // récupération du conteneur d'upload
            var container = cloudBlobClient.GetContainerReference("upload");
            await container.CreateIfNotExistsAsync();

            string fileName = Path.GetFileName(filePath);

            // récupération d'une référence vers un block blob
            var blockBlobReference = container.GetBlockBlobReference(fileName);

            using(var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // upload du fichier
                await blockBlobReference.UploadFromStreamAsync(fileStream);
            }

            return blockBlobReference.Uri;
        }
    }
}
