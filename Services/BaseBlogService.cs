namespace Miniblog.Core.AzureBlobStorage.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using System.Xml.XPath;
    using Microsoft.AspNetCore.Http;
    using Models;

    public abstract class BaseBlogService : IBlogService
    {
        private readonly List<Post> _cache = new List<Post>();
        private readonly IHttpContextAccessor _contextAccessor;

        protected BaseBlogService(IHttpContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        protected void RemoveFromCache(Post post)
        {
            if (_cache.Contains(post))
            {
                _cache.Remove(post);
            }
        }

        protected void AddToCache(Post post)
        {
            if (!_cache.Contains(post))
            {
                _cache.Add(post);
                sortCache();
            }
        }

        protected void Initialize()
        {
            loadPosts();
            sortCache();
        }

        private void loadPosts()
        {
            // Can this be done in parallel to speed it up?
            foreach (var file in GetPostFiles().Result)
            {
                var doc = XElement.Load(file);

                var post = new Post
                {
                    ID = Path.GetFileNameWithoutExtension(file),
                    Title = readValue(doc, "title"),
                    Excerpt = readValue(doc, "excerpt"),
                    Content = readValue(doc, "content"),
                    Slug = readValue(doc, "slug").ToLowerInvariant(),
                    PubDate = DateTime.Parse(readValue(doc, "pubDate")),
                    LastModified = DateTime.Parse(readValue(doc, "lastModified", DateTime.Now.ToString())),
                    IsPublished = bool.Parse(readValue(doc, "ispublished", "true"))
                };

                loadCategories(post, doc);
                loadComments(post, doc);
                _cache.Add(post);
            }
        }

        protected static XDocument CreatePostXmlFile(Post post)
        {
            var doc = new XDocument(
                new XElement("post",
                    new XElement("title", post.Title),
                    new XElement("slug", post.Slug),
                    new XElement("pubDate", post.PubDate.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XElement("lastModified", post.LastModified.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XElement("excerpt", post.Excerpt),
                    new XElement("content", post.Content),
                    new XElement("ispublished", post.IsPublished),
                    new XElement("categories", string.Empty),
                    new XElement("comments", string.Empty)
                ));

            var categories = doc.XPathSelectElement("post/categories");
            foreach (string category in post.Categories)
            {
                categories.Add(new XElement("category", category));
            }

            var comments = doc.XPathSelectElement("post/comments");
            foreach (var comment in post.Comments)
            {
                comments.Add(
                    new XElement("comment",
                        new XElement("author", comment.Author),
                        new XElement("email", comment.Email),
                        new XElement("date", comment.PubDate.ToString("yyyy-MM-dd HH:m:ss")),
                        new XElement("content", comment.Content),
                        new XAttribute("isAdmin", comment.IsAdmin),
                        new XAttribute("id", comment.ID)
                    ));
            }

            return doc;
        }

        private static void loadCategories(Post post, XContainer doc)
        {
            var categories = doc.Element("categories");
            if (categories == null)
            {
                return;
            }

            post.Categories = categories.Elements("category").Select(node => node.Value).ToArray();
        }

        private static void loadComments(Post post, XContainer doc)
        {
            var comments = doc.Element("comments");

            if (comments == null)
                return;

            foreach (var node in comments.Elements("comment"))
            {
                var comment = new Comment
                {
                    ID = readAttribute(node, "id"),
                    Author = readValue(node, "author"),
                    Email = readValue(node, "email"),
                    IsAdmin = bool.Parse(readAttribute(node, "isAdmin", "false")),
                    Content = readValue(node, "content"),
                    PubDate = DateTime.Parse(readValue(node, "date", "2000-01-01"))
                };

                post.Comments.Add(comment);
            }
        }

        private static string readValue(XContainer doc, XName name, string defaultValue = "")
        {
            return doc.Element(name) != null ? doc.Element(name)?.Value : defaultValue;
        }

        private static string readAttribute(XElement element, XName name, string defaultValue = "")
        {
            return element.Attribute(name) != null ? element.Attribute(name)?.Value : defaultValue;
        }

        private void sortCache()
        {
            _cache.Sort((p1, p2) => p2.PubDate.CompareTo(p1.PubDate));
        }

        private bool isAdmin()
        {
            return _contextAccessor.HttpContext?.User?.Identity.IsAuthenticated == true;
        }

        #region Implementation of IBlogService

        public Task<IEnumerable<Post>> GetPosts(int count, int skip = 0)
        {
            var isAdmin = this.isAdmin();

            var posts = _cache
                .Where(p => p.PubDate <= DateTime.UtcNow && (p.IsPublished || isAdmin))
                .Skip(skip)
                .Take(count);

            return Task.FromResult(posts);
        }

        public Task<IEnumerable<Post>> GetPostsByCategory(string category)
        {
            var isAdmin = this.isAdmin();

            var posts = from p in _cache
                where p.PubDate <= DateTime.UtcNow && (p.IsPublished || isAdmin)
                where p.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)
                select p;

            return Task.FromResult(posts);
        }

        public Task<Post> GetPostBySlug(string slug)
        {
            var post = _cache.FirstOrDefault(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            var isAdmin = this.isAdmin();

            if (post != null && post.PubDate <= DateTime.UtcNow && (post.IsPublished || isAdmin))
            {
                return Task.FromResult(post);
            }

            return Task.FromResult<Post>(null);
        }

        public Task<Post> GetPostById(string id)
        {
            var post = _cache.FirstOrDefault(p => p.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
            var isAdmin = this.isAdmin();

            if (post != null && post.PubDate <= DateTime.UtcNow && (post.IsPublished || isAdmin))
            {
                return Task.FromResult(post);
            }

            return Task.FromResult<Post>(null);
        }

        public Task<IEnumerable<string>> GetCategories()
        {
            var isAdmin = this.isAdmin();

            var categories = _cache
                .Where(p => p.IsPublished || isAdmin)
                .SelectMany(post => post.Categories)
                .Select(cat => cat.ToLowerInvariant())
                .Distinct();

            return Task.FromResult(categories);
        }

        protected abstract Task<IEnumerable<string>> GetPostFiles();

        public abstract Task SavePost(Post post);

        public abstract Task DeletePost(Post post);

        public abstract Task<string> SaveFile(byte[] bytes, string fileName, string suffix = null);

        #endregion
    }
}