// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.VisualTree;
using Froststrap.UI.Elements.Controls;

namespace Froststrap.UI.Utility
{
    /// <summary>
    /// Utility for building search indexes from UI elements.
    /// Scans majoirty of elements in search page (OptionControls, CardExpanders, CardActions) etc.
    /// Should be fairly competent at doing its job.
    /// </summary>
    internal partial class SearchIndexBuilder
    {
        private readonly Dictionary<string, List<SearchBarItem>> _pageIndexCache = [];
        private List<SearchBarItem>? _navigationItemsCache;


        public List<SearchBarItem> BuildIndex(List<(string PageTag, string PageTitle, object PageViewModel)> _)
        {
            List<SearchBarItem> searchIndex = [.. GetNavigationItems()];
            return searchIndex;
        }

        private List<SearchBarItem> GetNavigationItems()
        {
            if (_navigationItemsCache != null)
            {
                return _navigationItemsCache;
            }

            var navigationItems = NavigationRegistry.Pages.Where(page => page.Sidebar)
                .Select(page => new { Display = page.Title, Tag = page.Tag });

            _navigationItemsCache = [];
            foreach (var item in navigationItems)
            {
                _navigationItemsCache.Add(new SearchBarItem
                {
                    DisplayName = item.Display,
                    Tag = item.Tag,
                    NavigateAction = null,
                    PageTag = item.Tag,
                    Category = "Navigation"
                });
            }

            return _navigationItemsCache;
        }

        public void InvalidatePageCache(string pageTag)
        {
            _pageIndexCache.Remove(pageTag);
        }

        public void ClearCache()
        {
            _pageIndexCache.Clear();
            _navigationItemsCache = null;
        }

        public List<SearchBarItem> ScanRenderedPageForElements(Control pageView, string pageTag)
        {
            List<SearchBarItem> newItems = [];

            try
            {
                if (_pageIndexCache.ContainsKey(pageTag))
                {
                    return newItems;
                }

                ScanForCardExpanders(pageView, pageTag, "", newItems);
                ScanForOptionControls(pageView, pageTag, "", newItems);
                ScanForCardActions(pageView, pageTag, "", newItems);
                ScanForSquareCards(pageView, pageTag, "", newItems);

                if (newItems.Count > 0)
                {
                    _pageIndexCache[pageTag] = newItems;
                }

                return newItems;
            }
            catch (Exception ex)
            {
                App.Logger.Error($"Error scanning rendered page {pageTag}: {ex.Message}");
                return newItems;
            }
        }

        private static void ScanForCardExpanders(Control pageView, string pageTag, string pageTitle, List<SearchBarItem> searchIndex)
        {
            var cardExpanders = pageView.GetVisualDescendants()
                .OfType<CardExpander>().Where(IsIndexable);

            foreach (var expander in cardExpanders)
            {
                var header = expander.Header as string ?? (expander.Header as TextBlock)?.Text;
                if (!string.IsNullOrWhiteSpace(header))
                {
                    searchIndex.Add(new SearchBarItem
                    {
                        DisplayName = header,
                        Tag = NormalizeTag(header),
                        PageTag = pageTag,
                        PageTitle = pageTitle,
                        Category = "Section",
                        Description = expander.Description as string
                    });
                }
            }
        }

        private static void ScanForOptionControls(Control pageView, string pageTag, string pageTitle, List<SearchBarItem> searchIndex)
        {
            var optionControls = pageView.GetVisualDescendants()
                .OfType<OptionControl>().Where(IsIndexable);

            foreach (var option in optionControls)
            {
                var title = option.Header;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var parentExpander = FindParentCardExpander(option);
                    var parentSectionName = parentExpander?.Header as string;

                    searchIndex.Add(new SearchBarItem
                    {
                        DisplayName = title,
                        Tag = NormalizeTag(title),
                        PageTag = pageTag,
                        PageTitle = pageTitle,
                        Category = "Setting",
                        Description = option.Description,
                        ParentSectionName = parentSectionName
                    });
                }
            }
        }

        private static void ScanForCardActions(Control pageView, string pageTag, string pageTitle, List<SearchBarItem> searchIndex)
        {
            var cardActions = pageView.GetVisualDescendants()
                .OfType<CardAction>().Where(IsIndexable);

            foreach (var action in cardActions)
            {
                var content = string.IsNullOrWhiteSpace(action.Header) ? action.Content as string ?? (action.Content as TextBlock)?.Text : action.Header;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    searchIndex.Add(new SearchBarItem
                    {
                        DisplayName = content,
                        Tag = NormalizeTag(content),
                        PageTag = pageTag,
                        PageTitle = pageTitle,
                        Category = "Action",
                        Description = action.Description,
                        ParentSectionName = FindParentCardExpander(action)?.Header as string
                    });
                }
            }
        }

        private static void ScanForSquareCards(Control pageView, string pageTag, string pageTitle, List<SearchBarItem> searchIndex)
        {
            var squareCards = pageView.GetVisualDescendants()
                .OfType<SquareCard>().Where(IsIndexable);

            foreach (var card in squareCards)
            {
                var header = card.Header as string;
                if (!string.IsNullOrWhiteSpace(header))
                {
                    searchIndex.Add(new SearchBarItem
                    {
                        DisplayName = header,
                        Tag = NormalizeTag(header),
                        PageTag = pageTag,
                        PageTitle = pageTitle,
                        Category = "Card",
                        Description = card.Description as string,
                        ParentSectionName = FindParentCardExpander(card)?.Header as string
                    });
                }
            }
        }

        private static CardExpander? FindParentCardExpander(Control control)
            => control.GetVisualAncestors().OfType<CardExpander>().FirstOrDefault();
        private static bool IsIndexable(Control control) => control.IsVisible && control.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible);

        private static string NormalizeTag(string text)
        {
            return Regex.Replace(text.ToUpperInvariant(), @"[^A-Z0-9]+", "-").Trim('-');
        }
    }
}
