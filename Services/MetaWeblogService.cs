namespace Miniblog.Core.AzureBlobStorage.Services
{
    using System;
    using System.Linq;
    using System.Security.Claims;
    using Controllers;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using WilderMinds.MetaWeblog;

    public class MetaWeblogService : IMetaWeblogProvider
    {
        private readonly IBlogService _blogService;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _context;

        public MetaWeblogService(IBlogService blogService, IConfiguration config, IHttpContextAccessor context)
        {
            _blogService = blogService;
            _config = config;
            _context = context;
        }

        public string AddPost(string blogId, string username, string password, WilderMinds.MetaWeblog.Post post, bool publish)
        {
            validateUser(username, password);

            var newPost = new Models.Post
            {
                Title = post.title,
                Slug = !string.IsNullOrWhiteSpace(post.wp_slug) ? post.wp_slug : Models.Post.CreateSlug(post.title),
                Content = post.description,
                IsPublished = publish,
                Categories = post.categories
            };

            if (post.dateCreated != DateTime.MinValue)
            {
                newPost.PubDate = post.dateCreated;
            }

            _blogService.SavePost(newPost).GetAwaiter().GetResult();

            return newPost.ID;
        }

        public bool DeletePost(string key, string postId, string username, string password, bool publish)
        {
            validateUser(username, password);

            var post = _blogService.GetPostById(postId).GetAwaiter().GetResult();

            if (post != null)
            {
                _blogService.DeletePost(post).GetAwaiter().GetResult();
                return true;
            }

            return false;
        }

        public bool EditPost(string postId, string username, string password, Post post, bool publish)
        {
            validateUser(username, password);

            var existing = _blogService.GetPostById(postId).GetAwaiter().GetResult();

            if (existing == null)
            {
                return false;
            }

            existing.Title = post.title;
            existing.Slug = post.wp_slug;
            existing.Content = post.description;
            existing.IsPublished = publish;
            existing.Categories = post.categories;

            if (post.dateCreated != DateTime.MinValue)
            {
                existing.PubDate = post.dateCreated;
            }

            _blogService.SavePost(existing).GetAwaiter().GetResult();

            return true;

        }

        public CategoryInfo[] GetCategories(string blogId, string username, string password)
        {
            validateUser(username, password);

            return _blogService.GetCategories().GetAwaiter().GetResult()
                           .Select(cat =>
                               new CategoryInfo
                               {
                                   categoryid = cat,
                                   title = cat
                               })
                           .ToArray();
        }

        public Post GetPost(string postId, string username, string password)
        {
            validateUser(username, password);

            var post = _blogService.GetPostById(postId).GetAwaiter().GetResult();

            if (post != null)
            {
                return toMetaWebLogPost(post);
            }

            return null;
        }

        public Post[] GetRecentPosts(string blogId, string username, string password, int numberOfPosts)
        {
            validateUser(username, password);

            return _blogService.GetPosts(numberOfPosts).GetAwaiter().GetResult().Select(toMetaWebLogPost).ToArray();
        }

        public BlogInfo[] GetUsersBlogs(string key, string username, string password)
        {
            validateUser(username, password);

            var request = _context.HttpContext.Request;
            string url = request.Scheme + "://" + request.Host;

            return new[] { new BlogInfo {
                blogid ="1",
                blogName = _config["blog:name"],
                url = url
            }};
        }

        public MediaObjectInfo NewMediaObject(string blogId, string username, string password, MediaObject mediaObject)
        {
            validateUser(username, password);
            var bytes = Convert.FromBase64String(mediaObject.bits);
            var path = _blogService.SaveFile(bytes, mediaObject.name).GetAwaiter().GetResult();

            return new MediaObjectInfo { url = path };
        }

        public UserInfo GetUserInfo(string key, string username, string password)
        {
            validateUser(username, password);
            throw new NotImplementedException();
        }

        public int AddCategory(string key, string username, string password, NewCategory category)
        {
            validateUser(username, password);
            throw new NotImplementedException();
        }

        private void validateUser(string username, string password)
        {
            if (username != _config["user:username"] || !AccountController.VerifyHashedPassword(password, _config))
            {
                throw new MetaWeblogException("Unauthorized");
            }

            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.Name, _config["user:username"]));

            _context.HttpContext.User = new ClaimsPrincipal(identity);
        }

        private Post toMetaWebLogPost(Models.Post post)
        {
            var request = _context.HttpContext.Request;
            var url = request.Scheme + "://" + request.Host;

            return new Post
            {
                postid = post.ID,
                title = post.Title,
                wp_slug = post.Slug,
                permalink = url + post.GetLink(),
                dateCreated = post.PubDate,
                description = post.Content,
                categories = post.Categories.ToArray()
            };
        }
    }
}
