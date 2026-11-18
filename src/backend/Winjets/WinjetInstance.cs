// language: C# 12, file: WinjetInstance.cs, target: Windows 11 / .NET 8, modèle d'une instance posée sur le bureau
using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace PanneauxGlass.Winjets;

// Position en pixels logiques relatifs à la zone de travail de l'écran `Screen` (nom + taille), plus une position normalisée.
// `Screen` vide = écran principal. Les noms JSON sont ceux du contrat (docs/WINJETS-DESIGN.md §4).
internal sealed class WinjetGeometry
{
    [JsonPropertyName("screen")] public string Screen { get; set; } = "";
    [JsonPropertyName("x")]
    public double X
    {
        get; set;
    }
    [JsonPropertyName("y")]
    public double Y
    {
        get; set;
    }
    [JsonPropertyName("w")] public double W { get; set; } = 352;
    [JsonPropertyName("h")] public double H { get; set; } = 168;
    [JsonPropertyName("nx")]
    public double Nx
    {
        get; set;
    }
    [JsonPropertyName("ny")]
    public double Ny
    {
        get; set;
    }
    [JsonPropertyName("recovered")]
    public bool Recovered
    {
        get; set;
    }

    internal WinjetGeometry Copy() => (WinjetGeometry)MemberwiseClone();
}

internal sealed class WinjetInstance
{
    public string Id { get; set; } = "";
    public string PackageId { get; set; } = "";
    public bool Hidden
    {
        get; set;
    }
    public WinjetGeometry Geometry { get; set; } = new();
    // Opacité du fond de la carte, entier 0..100 ; null = suit le réglage de verre global.
    public int? Opacity
    {
        get; set;
    }
    // Valeurs bool, double ou string uniquement (validées contre widget.json).
    public Dictionary<string, object?> Settings { get; set; } = new();
    public Dictionary<string, object?> State { get; set; } = new();
}

// Fichier instances.json.
internal sealed class WinjetInstancesFile
{
    public int Version { get; set; } = 1;
    public List<WinjetInstance> Instances { get; set; } = new();
}
