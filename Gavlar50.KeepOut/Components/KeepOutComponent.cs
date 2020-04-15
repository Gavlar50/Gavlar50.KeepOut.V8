using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Security;
using Gavlar50.KeepOut.Models;
using Umbraco.Core.Composing;
using Umbraco.Core.Events;
using Umbraco.Core.Services;
using Umbraco.Web;
using Umbraco.Web.Trees;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.PropertyEditors.ValueConverters;
using Umbraco.Core.Services.Implement;

namespace Gavlar50.KeepOut.Components
{
    public class KeepOutComponent : IComponent
    {
        private readonly IUmbracoContextFactory _factory;
        private readonly ILogger _logger;
        private readonly IMemberGroupService _memberGroupService;

        public KeepOutComponent(IUmbracoContextFactory factory, ILogger logger, IMemberGroupService memberGroupService)
        {
            _factory = factory;
            _logger = logger;
            _memberGroupService = memberGroupService;
        }

        /// <summary>
        /// The list of rules as defined in Umbraco
        /// </summary>
        private List<KeepOutRule> Rules { get; set; }

        /// <summary>
        /// The secured page ids, used to check current page security. Stored as strings
        /// so we can intersect the page Path 
        /// </summary>
        private List<string> RulesPages { get; set; }

        /// <summary>
        /// The content node id of the rules folder
        /// </summary>
        private int KeepOutRulesFolderId { get; set; }

        /// <summary>
        /// Gets or sets whether the rules are visualised in the content tree
        /// </summary>
        private bool VisualiseRules { get; set; }

        public void Initialize()
        {
            using (var context = _factory.EnsureUmbracoContext())
            {
                //Listen for the ApplicationInit event which then allows us to bind to the HttpApplication events
                UmbracoApplicationBase.ApplicationInit += UmbracoApplication_ApplicationInit;

                // allow nodes to be coloured to visualise rule coverage
                TreeControllerBase.TreeNodesRendering += TreeControllerBase_TreeNodesRendering;
                
                // subscribe to the publish/delete events so rules can be reloaded
                ContentService.Published += ContentService_Published;
                ContentService.Trashed += ContentService_Trashed;

                // Load and set the config
                if (!RefreshConfig()) return;
                RefreshRules(); // Load and store the rules
                _logger.Info(GetType(), "KeepOut initialised");
            }
        }

        /// <summary>
        /// The ApplicationInit event which then allows us to bind to the HttpApplication events.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UmbracoApplication_ApplicationInit(object sender, EventArgs e)
        {
            var umbracoApp = (HttpApplication)sender;
            umbracoApp.PreRequestHandlerExecute += UmbracoApplication_PreRequestHandlerExecute;
        }

        /// <summary>
        /// The event that fires whenever a resource is requested, so we can check if it is allowed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UmbracoApplication_PreRequestHandlerExecute(object sender, EventArgs e)
        {
            // we can cast sender to HttpApplication to get the logged in member
            var httpApp = sender as HttpApplication;
            if (httpApp == null) return;
            var loggedInMember = httpApp.User.Identity.Name;
            if (string.IsNullOrEmpty(loggedInMember)) return;
            var memberRoles = Roles.GetRolesForUser(loggedInMember);
            if (!memberRoles.Any()) return;

            // attempt to get the umbraco context
            if (!(httpApp.Context.Items["Umbraco.Web.HybridUmbracoContextAccessor"] is UmbracoContext umbracoContext)) return;

            // stop now if this is an umbraco backend request
            if (!umbracoContext.IsFrontEndUmbracoRequest) return;

            //// First we check if the requested page is part of a rule
            var pageId = umbracoContext.PublishedRequest.PublishedContent.Id;
            var page = umbracoContext.Content.GetById(pageId);
            var path = page.Path.Split( ',').ToList();

            // if the current page should be secured, the page path will contain the root page that was secured
            // this is how we know that this is a descendant of the secured page
            var hasRule = RulesPages.Intersect(path).ToList();
            if (!hasRule.Any()) return;

            var ruleIndex = RulesPages.IndexOf(hasRule.Last()); // if multiple rules overlap, take the last, rules are cumulative
            var activeRule = Rules[ruleIndex];
            var appliesToUser = activeRule.DeniedMemberGroups.Intersect(memberRoles);
            if (!appliesToUser.Any()) return;

            // member is in a group that has been denied access, so redirect to the no access page defined by the rule
            var noAccessPage = umbracoContext.Content.GetById(activeRule.NoAccessPage);
            umbracoContext.HttpContext.Response.Redirect(noAccessPage.Url);
        }

        /// <summary>
        /// ensure rules and config are updated when keepout rules are published or trashed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ContentService_Published(IContentService sender, ContentPublishedEventArgs e)
        {
            if (!e.PublishedEntities.Any(x => x.ContentType.Alias.StartsWith("keepOutSecurityRule")) && KeepOutRulesFolderId > 0) return;
            Reload(e.Messages);
        }

        private void ContentService_Trashed(IContentService sender, MoveEventArgs<IContent> e)
        {
            if (!e.MoveInfoCollection.Any(x => x.Entity.ContentType.Alias.StartsWith("keepOutSecurityRule"))) return;
            Reload(e.Messages);
        }

        /// <summary>
        /// Rules visualisation in umbraco backoffice
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TreeControllerBase_TreeNodesRendering(TreeControllerBase sender, TreeNodesRenderingEventArgs e)
        {
            if (KeepOutRulesFolderId == 0) return;
            if (!VisualiseRules) return;
            if (sender.TreeAlias != "content") return;

            using (var context = _factory.EnsureUmbracoContext())
            {
                foreach (var node in e.Nodes)
                {
                    var intCurrentNodeId = int.Parse(node.Id.ToString());
                    var page = context.UmbracoContext.Content.GetById(intCurrentNodeId);
                    if (page == null) continue;

                    // if the current node is secured by a rule (or is the rule itself), colour the node
                    var isRule = page.ContentType.Alias == "keepOutSecurityRule";
                    var path = page.Path.Split(',').ToList();
                    var hasRule = RulesPages.Intersect(path).ToList();
                    if (!hasRule.Any() && !isRule) continue;

                    node.CssClasses.Add("keepout");
                    if (isRule)
                    {
                        // node is the rule itself
                        var ruleColour = page.GetProperty("coverageColour").GetValue() as ColorPickerValueConverter.PickedColor;
                        node.CssClasses.Add($"keepout-{ruleColour.Label}");
                        node.CssClasses.Add("keepoutrule");
                    }
                    else
                    {
                        // node is content that is covered by a rule
                        var ruleNdx = RulesPages.IndexOf(hasRule.Last()); // if multiple rules overlap, last wins
                        var activeRule = Rules[ruleNdx];
                        node.CssClasses.Add(activeRule.CoverageColour);
                    }
                }
            }
        }

        public void Terminate()
        {
        }

        /// <summary>
        /// Refresh the rules so any changes are reflected immediately without site restart
        /// </summary>
        private void RefreshRules()
        {
            using (var context = _factory.EnsureUmbracoContext())
            {
                Rules = new List<KeepOutRule>();
                RulesPages = new List<string>();
                var rulesFolder = context.UmbracoContext.Content.GetById(KeepOutRulesFolderId);
                var rules = rulesFolder.Children
                    .Where(x => x.IsPublished() && x.ContentType.Alias == "keepOutSecurityRule")
                    .OrderBy(x => x.CreateDate);
                foreach (var rule in rules)
                {
                    var coverageColour = rule.Properties.Single(x => x.Alias == "coverageColour").GetValue() as ColorPickerValueConverter.PickedColor;
                    var deniedMemberGroups = new List<string>();
                    var memberGroupIds = rule.Properties.Single(x => x.Alias == "deniedMemberGroups").GetValue().ToString().Split(new []{','}).ToList();
                    foreach (var memberGroupId in memberGroupIds)
                    {
                        var intMemberGroupId = int.Parse(memberGroupId);
                        var memberGroup = _memberGroupService.GetById(intMemberGroupId);
                        deniedMemberGroups.Add(memberGroup.Name); 
                    }
                    var noAccessPage = rule.Properties.Single(x => x.Alias == "noAccessPage").GetValue() as IPublishedContent;
                    var pageToSecure = rule.Properties.Single(x => x.Alias == "pageToSecure").GetValue() as IPublishedContent;
                    var keepOutRule = new KeepOutRule
                    {
                        CoverageColour = "keepout-" + coverageColour.Label,
                        DeniedMemberGroups = deniedMemberGroups,
                        NoAccessPage = noAccessPage.Id,
                        PageToSecure = pageToSecure.Id
                    };
                    Rules.Add(keepOutRule);
                    RulesPages.Add(keepOutRule.PageToSecure.ToString());
                }
            }
        }

        /// <summary>
        /// Refresh the config so any changes are reflected immediately without site restart
        /// </summary>
        /// <param name="e">The EventMessages collection, in case you need to feed back errors or info</param>
        /// <returns>True if config refresh was successful</returns>
        private bool RefreshConfig(EventMessages e = null)
        {
            using (var context = _factory.EnsureUmbracoContext())
            {
                VisualiseRules = false;
                KeepOutRulesFolderId = 0;
                var keepOutRulesFolder = context.UmbracoContext.Content.GetAtRoot(false).FirstOrDefault(x => x.ContentType.Alias == "keepOutSecurityRules");
                if (keepOutRulesFolder == null) return false;
                KeepOutRulesFolderId = keepOutRulesFolder.Id;
                VisualiseRules = (bool)keepOutRulesFolder.Properties.Single(x => x.Alias == "showRuleCoverage").GetValue();
                return true;
            }
        }

        /// <summary>
        /// Refreshes the config and rules
        /// </summary>
        /// <param name="e">The event message collection</param>
        private void Reload(EventMessages e)
        {
            if (!RefreshConfig(e)) return;
            RefreshRules();
            if (VisualiseRules) e.Add(new EventMessage("KeepOut Security", "KeepOut Security updated. Refresh the node tree to show changes",EventMessageType.Info));
        }
    }
}
