using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
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
            Console.WriteLine("Enter the path of the file to upload:");
            string path = Console.ReadLine();

            if (!File.Exists(path))
            {
                Console.WriteLine("The file was not found");
                return;
            }

            string storageConnectionString = ConfigurationManager.ConnectionStrings["AzureStorage"].ConnectionString;
            string uploadContainerName = "uploads";

            // create a cloud storage account and a cloud blob client
            CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(storageConnectionString);
            CloudBlobClient cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();

            // get the reference to the upload container and create it if it does not exist
            CloudBlobContainer container = cloudBlobClient.GetContainerReference(uploadContainerName);
            container.CreateIfNotExists();

            // get a block blob reference
            CloudBlockBlob blob = container.GetBlockBlobReference(Path.GetFileName(path));

            // upload the file
            blob.UploadFromFile(path, FileMode.Open);

            Console.WriteLine("The file has been upload to the blob {0}", blob.Uri);
        }
    }
}
