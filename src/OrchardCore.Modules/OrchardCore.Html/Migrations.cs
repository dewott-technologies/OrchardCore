using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.ContentManagement.Records;
using OrchardCore.Data.Migration;
using YesSql;

namespace OrchardCore.Html
{
    public class Migrations : DataMigration
    {
        private readonly ISession _session;
        private readonly ILogger<Migrations> _logger;
        private readonly IContentDefinitionManager _contentDefinitionManager;

        public Migrations(
            IContentDefinitionManager contentDefinitionManager,
            ISession session,
            ILogger<Migrations> logger)
        {
            _contentDefinitionManager = contentDefinitionManager;
            _session = session;
            _logger = logger;
        }


        public async Task<int> CreateAsync()
        {
            await _contentDefinitionManager.AlterPartDefinitionAsync("HtmlBodyPart", builder => builder
                .Attachable()
                .WithDescription("Provides an HTML Body for your content item."));

            return 2;
        }

        public int UpdateFrom2()
        {
            return 3;
        }

        public async Task<int> UpdateFrom3Async()
        {
            // This code can be removed in RC

            // Update content type definitions
            foreach (var contentType in await _contentDefinitionManager.ListTypeDefinitionsAsync())
            {
                if (contentType.Parts.Any(x => x.PartDefinition.Name == "BodyPart"))
                {
                    await _contentDefinitionManager.AlterTypeDefinitionAsync(contentType.Name, x => x.RemovePart("BodyPart").WithPart("HtmlBodyPart"));
                }
            }

            await _contentDefinitionManager.DeletePartDefinitionAsync("BodyPart");

            // We are patching all content item versions by moving the Title to DisplayText
            // This step doesn't need to be executed for a brand new site

            var lastDocumentId = 0;

            for (; ; )
            {
                var contentItemVersions = await _session.Query<ContentItem, ContentItemIndex>(x => x.DocumentId > lastDocumentId).Take(10).ListAsync();

                if (!contentItemVersions.Any())
                {
                    // No more content item version to process
                    break;
                }

                foreach (var contentItemVersion in contentItemVersions)
                {
                    if (UpdateBody(contentItemVersion.Content))
                    {
                        _session.Save(contentItemVersion);
                        _logger.LogInformation($"A content item version's BodyPart was upgraded: '{contentItemVersion.ContentItemVersionId}'");
                    }

                    lastDocumentId = contentItemVersion.Id;
                }

                await _session.CommitAsync();
            }

            bool UpdateBody(JToken content)
            {
                var changed = false;

                if (content.Type == JTokenType.Object)
                {
                    var body = content["BodyPart"]?["Body"]?.Value<string>();

                    if (!String.IsNullOrWhiteSpace(body))
                    {
                        content["HtmlBodyPart"] = new JObject(new JProperty("Html", body));
                        changed = true;
                    }
                }

                foreach (var token in content)
                {
                    changed = UpdateBody(token) || changed;
                }

                return changed;
            }

            return 4;
        }

    }
}
