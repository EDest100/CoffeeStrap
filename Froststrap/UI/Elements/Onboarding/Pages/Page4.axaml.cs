// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
using Froststrap.UI.ViewModels.Onboarding;
namespace Froststrap.UI.Elements.Onboarding.Pages;
internal partial class Page4 : UserControl
{
    public Page4() { InitializeComponent(); DataContext = new Page4ViewModel(); }
}
