// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: CoffeeStrap.ThemeBuilder <pristine-client-directory> <output-directory> <client-version>");
    return 2;
}
try
{
    CoffeeThemeBuilder.Build(args[0], args[1], args[2]);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
