# 📜 Hippocampe - Guide de Stratégie & Règles d'Or

Ce document sert de référence technique ultime pour le projet **SystemeCaisse**. Il recense les décisions d'architecture critiques, les optimisations de performance et les stratégies de stabilité pour éviter toute régression lors des futures évolutions.

---

## 🏗️ Architecture Globale
- **Framework** : .NET 8 WPF.
- **Pattern** : MVVM strict via `CommunityToolkit.Mvvm`.
- **Communication** : Les ViewModels communiquent avec la `MainWindow` via des méthodes dédiées (ex: `SetupWindowOwner`).
- **Style** : Thème moderne (vert #2E7D32) avec coins arrondis et glassmorphism.

---

## 🖥️ Stratégie Multi-Écran (DÉTERMINANT)
Le positionnement des fenêtres est le point le plus sensible. **Ne jamais modifier ces règles sans test multi-moniteur.**

### 1. La Règle "Normal avant Maximized"
Windows ignore souvent les coordonnées `Left` / `Top` si la fenêtre est déjà en `WindowState.Maximized`.
- **Règle** : Toujours repasser en `WindowState.Normal`, positionner la fenêtre via les coordonnées logiques, puis la passer en `Maximized`.
- **Application** : Voir `InitializeCustomerDisplay` et `MoveAdminToScreen`.

### 2. DPI Awareness & Coordonnées Logiques
L'application supporte des écrans avec des échelles différentes (ex: 100% et 125%).
- **Règle** : Ne jamais utiliser les pixels physiques de Win32 seuls. Utiliser `ScreenHelper.GetScreens()` qui calcule les **Coordonnées Logiques** (DPI-indépendantes) via `shcore.dll`.
- **Calcul** : `Logiciel = Physique / (DPI / 96)`.

### 3. Ancrage et Centrage des Modales
Les fenêtres de dialogue ne doivent **JAMAIS** s'ouvrir sur l'écran client.
- **Règle** : Utiliser `SetupWindowOwner(Window win)`. Elle calcule manuellement le centre de l'écran où se trouve actuellement la fenêtre **Admin** en utilisant les coordonnées logiques.
- **Avantage** : Évite que les modales ne soient perdues sur l'écran secondaire ou décalées par le DPI.

---

## 📊 Stratégies par Module

### 🛒 Caisse (SalesView)
- **Virtualisation** : Ne jamais désactiver la virtualisation UI pour les listes de produits (indispensable pour la fluidité).
- **Saisie Poids** : La fenêtre de poids doit être modale et forcer l'ancrage sur l'Admin.

### 📈 Analyses (AnalysisView)
- **Export Excel/PDF** : Le `SaveFileDialog` doit être rattaché à la `MainWindow` via `Owner = mainWin`.
- **Performance** : Les calculs de statistiques lourds (ex: ventes sur an) doivent être optimisés au niveau SQLite (Index sur `Date`).

### ⚙️ Configuration (SettingsView)
- **Mise à Jour Instantanée** : Le bouton "Appliquer" doit déclencher une réinitialisation complète des écrans sans redémarrer l'application.

---

## 🗄️ Gestion des Données & Sync
- **Base de donnée** : SQLite (`caisse.db`).
- **Sync Python** : L'installeur permet l'import d'une base `database.db` (Python) qui est convertie/renommée automatiquement.
- **Schéma** : Les clés étrangères (`FK_Produit_Rayon`) sont activées au démarrage pour garantir l'intégrité.

---

## 📦 Build & Installer
- **Publication** : Toujours utiliser `PublishSingleFile=true` et `SelfContained=true`.
- **Installer** : Le projet `Installer` embarque le POS (`payload.exe`) en tant que ressource intégrée.
- **Icones** : Le logo (`logo.png`) est copié dans `%LocalAppData%\Hippocampe\Images\` pour les raccourcis.

---

## 🚫 Anti-Patterns & Pièges Connus
- **Fichiers Lockés** : Lors du build, l'Admin doit être éteint car l'installeur utilise PowerShell pour tuer le processus.
- **Namespace UI** : Ne jamais référencer `SystemeCaisse.UI.MainWindow` en dur dans les services partagés ; privilégier l'injection ou le casting via `Application.Current.Windows`.

---
*Ce document résume l'intelligence collective accumulée sur le projet. À consulter avant chaque modification structurelle.*
