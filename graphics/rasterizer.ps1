# Read an optional numeric SVG attribute using culture-independent parsing.
function Get-Number($Element, [string]$Name, [single]$Default = 0) {
    $value = $Element.GetAttribute($Name)
    if ($value -eq '') { return $Default }
    return [single]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

# Convert an SVG points list into the native drawing point representation.
function Get-Points([string]$Value) {
    $values = $Value.Trim() -split '[,\s]+'
    $points = [Collections.Generic.List[Drawing.PointF]]::new()
    for ($i = 0; $i -lt $values.Length; $i += 2) {
        $x = [single]::Parse($values[$i], [Globalization.CultureInfo]::InvariantCulture)
        $y = [single]::Parse($values[$i+1], [Globalization.CultureInfo]::InvariantCulture)
        $points.Add([Drawing.PointF]::new($x, $y))
    }
    return ,$points.ToArray()
}

# Rasterise this set's SVG primitives at four times the requested output size.
function Export-Icon([string]$Svg, [int]$Size, [string]$Destination) {
    $doc = [xml]$Svg
    $large = [Drawing.Bitmap]::new($Size*4, $Size*4)
    $g = [Drawing.Graphics]::FromImage($large)
    $g.Clear([Drawing.Color]::Transparent)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($Size*4/24.0, $Size*4/24.0)
    foreach ($element in $doc.DocumentElement.ChildNodes) {
        if ($element -isnot [Xml.XmlElement]) { continue }
        $shape = [Drawing.Drawing2D.GraphicsPath]::new()
        switch ($element.LocalName) {
            'line' { $shape.AddLine((Get-Number $element 'x1'), (Get-Number $element 'y1'), (Get-Number $element 'x2'), (Get-Number $element 'y2')) }
            'polyline' { $shape.AddLines((Get-Points $element.GetAttribute('points'))) }
            'polygon' { $shape.AddPolygon((Get-Points $element.GetAttribute('points'))) }
            'circle' {
                $r = Get-Number $element 'r'
                $shape.AddEllipse((Get-Number $element 'cx')-$r, (Get-Number $element 'cy')-$r, 2*$r, 2*$r)
            }
            'rect' { $shape.AddRectangle([Drawing.RectangleF]::new((Get-Number $element 'x'), (Get-Number $element 'y'), (Get-Number $element 'width'), (Get-Number $element 'height'))) }
            default { throw "Unsupported SVG primitive: $($element.LocalName)" }
        }
        $fill = $element.GetAttribute('fill')
        if ($fill -and $fill -ne 'none') {
            $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($fill))
            $g.FillPath($brush, $shape)
            $brush.Dispose()
        }
        $stroke = $element.GetAttribute('stroke')
        if (!$stroke) { $stroke = $ink }
        if ($stroke -ne 'none') {
            $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($stroke), (Get-Number $element 'stroke-width' 1.6))
            $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $shape)
            $pen.Dispose()
        }
        $shape.Dispose()
    }
    $g.Dispose()
    $small = [Drawing.Bitmap]::new($Size, $Size)
    $outGraphics = [Drawing.Graphics]::FromImage($small)
    $outGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $outGraphics.DrawImage($large, 0, 0, $Size, $Size)
    $small.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    $outGraphics.Dispose()
    $small.Dispose()
    $large.Dispose()
}
