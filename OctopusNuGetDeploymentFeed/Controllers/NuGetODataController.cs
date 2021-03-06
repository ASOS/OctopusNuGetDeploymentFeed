﻿using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading;
using System.Web.Http;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;
using OctopusDeployNuGetFeed.DataServices;
using OctopusDeployNuGetFeed.OData;

namespace OctopusDeployNuGetFeed.Controllers
{
    [NuGetODataControllerConfiguration]
    [Authorize]
    public class NuGetODataController : ODataController
    {
        private const int MaxPageSize = 25;
        private readonly IPackageRepositoryFactory _packageRepositoryFactory;

        public NuGetODataController(IPackageRepositoryFactory packageRepositoryFactory)
        {
            _packageRepositoryFactory = packageRepositoryFactory;
        }

        // GET /Packages(Id=,Version=)
        [HttpGet]
        public IHttpActionResult Get(ODataQueryOptions<ODataPackage> options, string id, string version, CancellationToken token)
        {
            var serverRepository = _packageRepositoryFactory.GetPackageRepository(User);
            if (!serverRepository.IsAuthenticated)
                return StatusCode(HttpStatusCode.Forbidden);

            var package = serverRepository.GetOctopusReleasePackage(id, version, token);
            if (package == null)
                return NotFound();

            return TransformToQueryResult(options, new[] {package}).FormattedAsSingleResult<ODataPackage>();
        }

        // GET/POST /FindPackagesById()?id=
        [HttpGet]
        [HttpPost]
        public IHttpActionResult FindPackagesById(ODataQueryOptions<ODataPackage> options, [FromODataUri] string id, [FromUri] string semVerLevel = "", CancellationToken token = default(CancellationToken))
        {
            var serverRepository = _packageRepositoryFactory.GetPackageRepository(User);
            if (!serverRepository.IsAuthenticated)
                return StatusCode(HttpStatusCode.Forbidden);

            if (string.IsNullOrEmpty(id))
                return QueryResult(options, Enumerable.Empty<ODataPackage>().AsQueryable(), MaxPageSize);

            var sourceQuery = serverRepository.FindOctopusReleasePackages(id, token);

            return TransformToQueryResult(options, sourceQuery);
        }

        // GET/POST /Search()?searchTerm=&targetFramework=&includePrerelease=
        [HttpGet]
        [HttpPost]
        public IHttpActionResult Search(ODataQueryOptions<ODataPackage> options, [FromODataUri] string searchTerm = "", [FromODataUri] string targetFramework = "", [FromODataUri] bool includePrerelease = false, [FromODataUri] bool includeDelisted = false, [FromUri] string semVerLevel = "", CancellationToken token = default(CancellationToken))
        {
            var serverRepository = _packageRepositoryFactory.GetPackageRepository(User);
            if (!serverRepository.IsAuthenticated)
                return StatusCode(HttpStatusCode.Forbidden);

            var sourceQuery = serverRepository.FindOctopusProjectPackages(searchTerm, token);

            return TransformToQueryResult(options, sourceQuery);
        }

        // Exposed as OData Action for specific entity GET/HEAD /Packages(Id=,Version=)/Download
        [HttpGet]
        [HttpHead]
        public HttpResponseMessage Download(string id, string version = "", CancellationToken token = default(CancellationToken))
        {
            var serverRepository = _packageRepositoryFactory.GetPackageRepository(User);
            if (!serverRepository.IsAuthenticated)
                return Request.CreateErrorResponse(HttpStatusCode.Forbidden, "Not authenticated");

            var requestedPackage = serverRepository.GetOctopusReleasePackage(id, version, token);
            if (requestedPackage == null)
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"'Package {id} {version}' Not found.");

            var responseMessage = Request.CreateResponse(HttpStatusCode.OK);

            if (Request.Method == HttpMethod.Get)
                responseMessage.Content = new StreamContent(requestedPackage.GetStream());
            else
                responseMessage.Content = new StringContent(string.Empty);

            responseMessage.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue("binary/octet-stream");
            responseMessage.Content.Headers.LastModified = requestedPackage.Published;

            responseMessage.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue(DispositionTypeNames.Attachment)
            {
                FileName = $"{requestedPackage.Id}.{requestedPackage.Version}.{Constants.PackageExtension}",
                Size = requestedPackage.PackageSize,
                ModificationDate = responseMessage.Content.Headers.LastModified
            };

            return responseMessage;
        }

        private IHttpActionResult QueryResult<TModel>(ODataQueryOptions<TModel> options, IQueryable<TModel> queryable, int maxPageSize)
        {
            return new QueryResult<TModel>(options, queryable, this, maxPageSize);
        }

        private IHttpActionResult TransformToQueryResult(ODataQueryOptions<ODataPackage> options, IEnumerable<INuGetPackage> sourceQuery)
        {
            return QueryResult(options, sourceQuery.Distinct().Select(AsODataPackage).AsQueryable(), MaxPageSize);
        }

        private static ODataPackage AsODataPackage(INuGetPackage package)
        {
            return new ODataPackage
            {
                Id = package.Id,
                Version = package.Version,
                Summary = package.Summary,
                IsAbsoluteLatestVersion = package.IsAbsoluteLatestVersion,
                IsLatestVersion = package.IsLatestVersion,
                Published = package.Published,
                Authors = package.Authors,
                Description = package.Description,
                Listed = package.Listed,
                ReleaseNotes = package.ReleaseNotes,
                Title = package.Title
            };
        }
    }
}