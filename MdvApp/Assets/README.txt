Asset folder for MdvApp.

sinclair.svg
    The SINCLAIR wordmark. Its vector paths are embedded directly into the Home
    banner (HomePage.xaml) as a WPF <Path>, so WPF renders it without an SVG
    library and the .svg file itself is not loaded at runtime.

Any .jpg / .png dropped here is copied next to the exe on build (see MdvApp.csproj),
so future pages can load raster assets by path if needed.
