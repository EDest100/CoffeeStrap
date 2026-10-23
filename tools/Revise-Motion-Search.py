from pathlib import Path
from xml.dom import minidom as D
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
p='Froststrap/UI/Elements/Settings/MainWindow.axaml';s=read(p).replace('<transitions:FluentEntranceTransition VerticalOffset="24" Duration="0:0:0.22"/>','<transitions:CoffeePageTransition/>').replace('<transitions:FluentEntranceTransition VerticalOffset="24" Duration="0:0:0.22" />','<transitions:CoffeePageTransition />');write(p,s)
p='Froststrap/UI/Elements/Settings/MainWindow.axaml.cs';s=read(p)
start=s.index('        private void ScrollToSearchItem(');brace=s.index('{',start);end=brace+1;depth=1
while depth:
 if s[end]=='{':depth+=1
 elif s[end]=='}':depth-=1
 end+=1
s=s[:start]+'''        private CancellationTokenSource? _searchRevealCancellation;
        internal Control? LastSearchFocusTarget { get; private set; }
        private async void ScrollToSearchItem(SearchBarItem item)
        {
            _searchRevealCancellation?.Cancel();
            _searchRevealCancellation?.Dispose();
            var cancellation = _searchRevealCancellation = new CancellationTokenSource();
            try
            {
                var page = this.FindControl<TransitioningContentControl>("PageContentControl")?.Content as Control;
                if (page == null) return;
                Control? target = page.GetVisualDescendants().OfType<OptionControl>().FirstOrDefault(card => card.Header == item.DisplayName)
                    ?? (Control?)page.GetVisualDescendants().OfType<CardExpander>().FirstOrDefault(group => Equals(group.Header, item.DisplayName));
                if (target == null) return;
                foreach (var group in target.GetVisualAncestors().OfType<CardExpander>()) group.IsExpanded = true;
                if (CoffeeMotion.Enabled) await Task.Delay(250, cancellation.Token);
                if (cancellation.IsCancellationRequested || !ReferenceEquals(page, this.FindControl<TransitioningContentControl>("PageContentControl")?.Content)) return;
                target.BringIntoView();
                var focus = target is OptionControl option && option.Choice != null ? option.Choice
                    : target.Focusable ? target : target.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Focusable && control.IsEffectivelyEnabled);
                focus ??= target; focus.Focusable = true;
                focus.Focus(NavigationMethod.Tab);
                LastSearchFocusTarget = focus;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { App.Logger.Error(error, "Could not reveal a settings search result."); }
        }'''+s[end:]
# A search result is obsolete if a newer query was typed while the debouncer dispatched it.
write(p,s)
p='Froststrap/UI/ViewModels/SearchBarViewModel.cs';s=read(p).replace('''                        if (!token.IsCancellationRequested)
                            FilterSearchResults();''','''                        if (!token.IsCancellationRequested)
                            await Dispatcher.UIThread.InvokeAsync(FilterSearchResults);''')
s=s.replace('''                FilteredSearchResults = new ObservableCollection<SearchBarItem>(filtered);''','''                if (query != SearchQuery || _disposed) return;
                FilteredSearchResults = new ObservableCollection<SearchBarItem>(filtered);''')
write(p,s)
# Remove the obsolete region toggle/group; the only public preference lives on Home.
p='Froststrap/UI/Elements/Settings/Pages/BehaviourPage.axaml';doc=D.parseString(read(p))
for node in list(doc.getElementsByTagName('controls:CardExpander'))+list(doc.getElementsByTagName('controls:OptionControl')):
 if node.parentNode and any('BetterMatchmaking' in a.value for a in [node.attributes.item(i) for i in range(node.attributes.length)]):node.parentNode.removeChild(node)
write(p,'<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n SPDX-License-Identifier: MPL-2.0 -->\n'+doc.documentElement.toxml())
print('Cancelable page transition, search reveal/focus and retired region UI cleaned up.')
