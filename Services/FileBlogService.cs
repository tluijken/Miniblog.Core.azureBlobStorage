namespace Miniblog.Core.AzureBlobStorage.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Models;

    public class FileBlogService : BaseBlogService
    {
        private readonly string _folder;

        public FileBlogService(IHostingEnvironment env, IHttpContextAccessor contextAccessor) : base(contextAccessor)
        {
            _folder = Path.Combine(env.WebRootPath, "posts");
            Initialize();
        }

        public override Task DeletePost(Post post)
        {
            var filePath = getFilePath(post);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            RemoveFromCache(post);
            return Task.CompletedTask;
        }

        public override async Task SavePost(Post post)
        {
            var filePath = getFilePath(post);
            post.LastModified = DateTime.UtcNow;

            var doc = CreatePostXmlFile(post);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite))
            {
                await doc.SaveAsync(fs, SaveOptions.None, CancellationToken.None).ConfigureAwait(false);
            }

            AddToCache(post);
        }

        public override async Task<string> SaveFile(byte[] bytes, string fileName, string suffix = null)
        {
            suffix = suffix ?? DateTime.UtcNow.Ticks.ToString();

            var ext = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);

            var relative = $"files/{name}_{suffix}{ext}";
            var absolute = Path.Combine(_folder, relative);
            var dir = Path.GetDirectoryName(absolute);

            Directory.CreateDirectory(dir);
            using (var writer = new FileStream(absolute, FileMode.CreateNew))
            {
                await writer.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }

            return "/posts/" + relative;
        }

        protected override Task<IEnumerable<string>> GetPostFiles()
        {
            if (!Directory.Exists(_folder))
            {
                Directory.CreateDirectory(_folder);
            }

            return Task.FromResult(Directory.EnumerateFiles(_folder, "*.xml", SearchOption.TopDirectoryOnly));
        }

        private string getFilePath(Post post)
        {
            return Path.Combine(_folder, post.ID + ".xml");
        }
    }
}