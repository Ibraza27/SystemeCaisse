# Contexte du Projet SystemeCaisse

Ce fichier documente les retours d'expérience, les problèmes rencontrés lors des déploiements et les solutions trouvées au fil du développement. Il doit être consulté pour éviter de reproduire les mêmes erreurs.

## 1. Déploiement Self-Contained (.NET 8)
**Problème :** Après l'installation d'une nouvelle version, les utilisateurs voyaient un popup Windows leur demandant d'installer ".NET Desktop Runtime 8.0.x", bien que la commande `dotnet publish` incluait le paramètre `--self-contained true`.
**Cause :** En .NET 8, passer le flag en ligne de commande peut ne pas suffire si d'autres propriétés (comme WPF) interfèrent. Le build produit alors un exécutable "Framework-dependent" de 188 Ko au lieu d'intégrer le runtime.
**Solution :** Il faut **obligatoirement** ajouter `<SelfContained>true</SelfContained>` dans le `<PropertyGroup>` du fichier `SystemeCaisse.UI.csproj`. C'est la seule garantie d'obtenir une vraie application autonome.

## 2. Fausse "Perte de Données" (Désynchronisation Base Réseau)
**Problème :** Après la mise à jour via l'installateur, l'utilisateur ouvre la caisse et constate que toutes ses ventes, commandes et produits ont disparu. Seuls 3 produits de test ("Pomme Golden", etc.) sont visibles.
**Cause :** 
- L'installateur Inno Setup installe l'application dans un nouveau dossier propre (ex: `C:\Program Files (x86)\SystemeCaisse`).
- Le fichier `network_db.json` (qui indique à l'application de se connecter à la base réseau de la caisse principale) n'est pas présent dans ce nouveau dossier.
- En l'absence de ce fichier, l'application bascule par défaut en **Mode Local** et crée une nouvelle base de données `caisse.db` vide dans son dossier d'installation, générant les produits de test.
- Les vraies données ne sont pas perdues, elles sont toujours dans le dossier réseau partagé (ex: `\\100.113.56.25\SystemeCaisse\caisse.db`).
**Solution :** 
- Ne pas paniquer. Il suffit d'aller dans **Paramètres > Réseau**, d'activer le Mode Réseau et de pointer vers le chemin du dossier partagé.
- Des messages d'erreur détaillés ont été ajoutés dans l'UI (tests de synchro) pour expliquer ce phénomène de repli en Mode Local et rassurer l'utilisateur.

## 3. Déconnexion Web / Commandes TransitManager Manquantes
**Problème :** Le test de synchronisation Web échoue avec un message "Non connecté", et aucune commande en ligne n'arrive dans l'onglet Commandes.
**Cause :** Les clés d'activation de la synchronisation (`sync_enabled`, `sync_hub_url`, `sync_api_key`) sont stockées *à l'intérieur* de la base de données SQLite (table `Configuration`). Si l'on active la synchronisation sur une base de développement, cela n'active pas magiquement la base de production sur la caisse principale.
**Solution :** Le script d'activation (`activate_sync_prod.py`) doit être exécuté en ciblant explicitement le fichier `caisse.db` de production (ex: `\\100.113.56.25\SystemeCaisse\caisse.db`). Un redémarrage de l'application est ensuite requis pour relire la configuration.

## 4. "Perte" de l'Historique des Ventes (Filtres UI)
**Problème :** L'utilisateur ouvre l'onglet "Historique des ventes" et voit une liste vide, pensant avoir perdu toutes ses ventes passées.
**Cause :** Le `HistoryViewModel` filtre par défaut les ventes sur `DateTime.Today` (Aujourd'hui). Si aucune vente n'a été faite le jour de l'installation de la mise à jour, la liste est logiquement vide.
**Solution :** Dire à l'utilisateur de changer le filtre de date sur "Ce mois" ou "Cette année" pour voir réapparaître ses milliers de ventes.

## 5. Résilience de l'Installateur (Inno Setup)
**Règle :** L'installateur ne supprime jamais les bases de données `.db` ou les `.json` qui ne font pas partie du package généré par `dotnet publish`. C'est une sécurité vitale qui empêche l'écrasement accidentel des données si l'utilisateur installe l'application directement dans le dossier réseau partagé.

## 6. Refonte Multi-Poste (PostgreSQL + SQLite) et Unification des Fabriques
**Problème :** Lors du passage au moteur PostgreSQL, l'erreur `42P01: relation "Produits" does not exist` apparaissait et les onglets Produits, Stocks et Ventes étaient vides.
**Cause :** 
- L'injection de dépendances dans `App.xaml.cs` enregistrait deux fabriques en parallèle : un `AddDbContextFactory<AppDbContext>` pointant vers SQLite, et `CaisseDbContextFactory` pointant vers PostgreSQL. Les ViewModels recevaient des contextes ciblant des bases différentes (split-brain).
- De plus, les migrations EF Core n'étaient jouées qu'au démarrage sur SQLite, laissant la base PostgreSQL cible avec 0 tables créées.
**Solution :**
- `CaisseDbContextFactory` est l'unique source de vérité pour toute l'application. Elle est enregistrée comme singleton pour son type concret et pour l'interface `IDbContextFactory<AppDbContext>`.
- Le démarrage dans `App.xaml.cs` fait appel à `DatabaseBootstrapper.InitializeAsync()`, qui gère les migrations de schéma avec verrou consultatif PostgreSQL (`pg_advisory_lock`), garantissant la cohérence même si plusieurs postes démarrent en même temps.
- En cas de base cible neuve/vide, `DataTransferService.ImportSqliteAsync` est déclenché automatiquement pour migrer l'intégralité des données existantes (produits, ventes, commandes, historiques, images) depuis le fichier `caisse.db`.

## 7. Fluidité de Navigation des Onglets (WPF TabControl Caching)
**Problème :** Le passage entre les onglets présentait des ralentissements, des micro-gels de l'interface et des rechargements intempestifs.
**Cause :**
- Le `TabControl` natif de WPF détruit l'arborescence visuelle (`VisualTree`) de l'onglet actif et reconstruit de zéro l'arborescence de l'onglet cible à chaque clic. Sur des vues avec de lourds DataGrids (plus de 1 300 produits avec images, 3 700 ventes), cela provoque un coût de layout et de rendu important.
- Certains chargements s'exécutaient de manière synchrone sur le thread UI lors du basculement d'onglet.
**Solution :**
- Implémentation du composant `TabContent.IsCached="True"` (`SystemeCaisse.UI.Controls.TabContent`) sur le `TabControl` principal. Les arborescences visuelles sont conservées en mémoire et seule la visibilité (`Visibility.Visible` / `Visibility.Collapsed`) bascule. Le temps de passage d'un onglet à un autre passe à 0 ms.
- Déport des lectures en base hors du thread UI (`Task.Run` / chargements asynchrones dans `MainViewModel.cs`).
- Remplacement des popups d'erreur bloquantes (`MessageBox.Show`) par du logging silencieux `AppLog.Error` lors des rafraîchissements en arrière-plan.

