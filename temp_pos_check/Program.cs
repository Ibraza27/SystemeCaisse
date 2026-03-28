using System;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Infrastructure.Data;
using System.Linq;
using System.Collections.Generic;

// Absolute path to the database to be safe
var dbPath = @"s:\PROGRAMATION\SystemeCaisse\SystemeCaisse.UI\bin\Debug\net8.0-windows10.0.19041\caisse.db";
var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseSqlite($"Data Source={dbPath}");

using var context = new AppDbContext(optionsBuilder.Options);

// Fetch only primitive types to avoid ANY navigation property issues
var allSales = context.LignesVente.AsNoTracking()
    .Select(l => new { l.ProduitId, l.ProduitNom, l.Quantite })
    .ToList();

var tomateSales = allSales
    .Where(l => l.ProduitNom != null && l.ProduitNom.Contains("Tomate", StringComparison.OrdinalIgnoreCase))
    .GroupBy(l => new { l.ProduitId, l.ProduitNom })
    .Select(g => new { g.Key.ProduitId, g.Key.ProduitNom, Total = g.Sum(x => x.Quantite) })
    .OrderByDescending(x => x.Total)
    .ToList();

Console.WriteLine("Sales for 'Tomate' grouped by ID and Name (In-Memory, Safe):");
foreach (var s in tomateSales)
{
    Console.WriteLine($"- ID: {(s.ProduitId.HasValue ? s.ProduitId.Value.ToString() : "NULL")} | Name: {s.ProduitNom} | Total: {s.Total}");
}

var allProds = context.Produits.AsNoTracking().Select(p => new { p.Id, p.Nom, p.Actif }).ToList();
var matchedProds = allProds.Where(p => p.Nom != null && p.Nom.Contains("Tomate", StringComparison.OrdinalIgnoreCase)).ToList();

Console.WriteLine($"\nFound {matchedProds.Count} products containing 'Tomate' in Produits table:");
foreach (var p in matchedProds)
{
    Console.WriteLine($"- ID={p.Id}, Name='{p.Nom}', Active={p.Actif}");
}
