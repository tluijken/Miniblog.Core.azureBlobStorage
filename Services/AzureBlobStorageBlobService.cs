using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Miniblog.Core.AzureBlobStorage.Services
{
    using System.IO;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Blob;
    using Models;

    public class AzureBlobStorageBlobService : BaseBlogService
    {
        private const string AZURE_BLOB_STORAGE_CONNECTION_STRING_NAME = "AzureBlobStorageConnectionString";
        private readonly string _azureBlobContainerName;
        private readonly CloudBlobClient _blobClient;

        public AzureBlobStorageBlobService(IHttpContextAccessor contextAccessor, IConfiguration configuration) : base(contextAccessor)
        {
            var connectionString = configuration.GetConnectionString(AZURE_BLOB_STORAGE_CONNECTION_STRING_NAME);

            var storageAccount = CloudStorageAccount.Parse(connectionString);

            // Create the blob client.
            _blobClient = storageAccount.CreateCloudBlobClient();

            _azureBlobContainerName = configuration.GetValue<string>("AzureBlobContainerName");
            Initialize();
        }

        #region Overrides of BaseBlogService

        protected override async Task<IEnumerable<string>> GetPostFiles()
        {
            // Get the blob container.
            var container = await getBlobContainer();

            // List all the blobs from the container.
            var blobs = await container.ListBlobsSegmentedAsync(null);
            
            // return the blob that end with the xml file extension and return the absolute uri's
            return blobs.Results.Where(d => d.Uri.AbsoluteUri.EndsWith(".xml")).Select(d => d.Uri.AbsoluteUri);
        }

        public override async Task SavePost(Post post)
        {
            post.LastModified = DateTime.UtcNow;

            // Use the base class to create our XmlDocument.
            var doc = CreatePostXmlFile(post);

            // Resolve our blob storage container.
            var container = await getBlobContainer();

            // Determine the file name.
            var fileName = Path.Combine(post.ID + ".xml");

            // Create reference to a Blob that may or may not exist under the container
            var blockBlob = container.GetBlockBlobReference(fileName);
            
            // And upload the XmlDocument as a string value to that reference.
            await blockBlob.UploadTextAsync(doc.ToString());

            // Base implementation call for adding the new or updated blog item to cache.
            AddToCache(post);
        }

        public override async Task DeletePost(Post post)
        {
            // Determine the file name.
            var fileName = Path.Combine(post.ID + ".xml");

            // Resolve our blob storage container.
            var container = await getBlobContainer();

            // Create reference to a Blob that may or may not exist under the container
            var blockBlob = container.GetBlockBlobReference(fileName);

            // Delete the blob from Azure storage.
            await blockBlob.DeleteAsync();

            // Use base class implementation to remove the post from cache.
            RemoveFromCache(post);
        }

        public override async Task<string> SaveFile(byte[] bytes, string fileName, string suffix = null)
        {
            // Filename determination, same as the FileBlogStorage implementation (could be moved to base class)
            suffix = suffix ?? DateTime.UtcNow.Ticks.ToString();

            var ext = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);

            var relative = $"{name}_{suffix}{ext}";

            // Get the container;
            var container = await getBlobContainer();

            // Create reference to a Blob that may or may not exist under the container
            var blockBlob = container.GetBlockBlobReference(relative);

            // Upload the byte array argument to the blob reference we just created.
            await blockBlob.UploadFromByteArrayAsync(bytes, 0, bytes.Length);

            // return the absolute uri of the blob.
            return blockBlob.Uri.AbsoluteUri;
        }

        #endregion

        #region private helper functions

        private async Task<CloudBlobContainer> getBlobContainer()
        {
            var container = _blobClient.GetContainerReference(_azureBlobContainerName);

            await container.CreateIfNotExistsAsync();
            var permissions = await container.GetPermissionsAsync();
            permissions.PublicAccess = BlobContainerPublicAccessType.Blob;
            await container.SetPermissionsAsync(permissions);
            return container;
        }
        #endregion
    }
}
