// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
using Froststrap.UI.ViewModels.Onboarding;
namespace Froststrap.UI.Elements.Onboarding.Pages;

internal partial class Page7 : UserControl
{
    public Page7()
    {
        DataContext = new Page7ViewModel();
        InitializeComponent();
    }
}
