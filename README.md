# XamarinForms.Maps.Android.TemporaryPatch [![NuGet Status](https://img.shields.io/nuget/v/XamarinForms.Maps.Android.TemporaryPatch.svg)](https://www.nuget.org/packages/XamarinForms.Maps.Android.TemporaryPatch)
Temporary Patch for Issue https://bugzilla.xamarin.com/show_bug.cgi?id=52625

To use this workaround, you need to subclass the Xamarin Forms Map class, and add the renderer to your main activity's namespace:

    [assembly: ExportRenderer (typeof (CustomMap), typeof (XamarinForms.Maps.Android.TemporaryPatch.MapRenderer))]
    
The type CustomMap must be a subclass of Xamarin.Forms.Map.

[![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.me/spiegelsoft)
