# Contexte du Projet SystemeCaisse

Ce fichier documente les retours d'expérience, les problèmes rencontrés lors des déploiements et les solutions trouvées au fil du développement. Il doit être consulté pour éviter de reproduire les mêmes erreurs.

## 1. Déploiement Self-Contained (.NET 8)
**Problème :** Après l'installation d'une nouvelle version, les utilisateurs voyaient un popup Windows leur demandant d'installer ".NET Desktop Runtime 8.0.x", bien que la commande `dotnet publish` incluait le paramètre `--self-contained true`.
**Cause :** En .NET 8, passer le flag en ligne de commande peut ne pas suffire si d'autres propriétés (comme WPF) interfèrent. Le build produit alors un exécutable "Framework-dependent" de 188 Ko au lieu d'intégrer le runtime.
**Solution :** Il faut **obligatoirement** ajouter `<SelfContained>true</SelfContained>` dans le `<PropertyGroup>` du fichier `SystemeCaisse.UI.csproj`. C'est la seule garantie d'obtenir une vraie application autonome.

## 2. Fausse "Perte de Données" (Désynchronisation Base Réseau)
**Problème :** Après la mise à jour via l'installateur, l'utilisateur ouvre la caisse et constate que toutes ses ventes, commandes et produits ont disparu. Seuls 3 produits de test ("Pomme Golden", etc.) sont visibles.
**Cause :** 
- L'installateur Inno Setup installe l'application dans un nouveau dossier propre (ex: `C:\Program Files\SystemeCaisse`).
- La configuration globale est désormais stockée dans `C:\ProgramData\SystemeCaisse\config\poste.json` afin de survivre aux réinstallations et mises à jour.
- Si un poste neuf n'est pas encore configuré pour pointer vers le Poste Principal, il démarre par défaut en mode local.
**Solution :** 
- Aller dans **Paramètres > 🔧 Système > 🌐 Multi-Postes PostgreSQL & Tailscale**, sélectionner **Poste Secondaire**, saisir l'adresse IP Tailscale du Poste Principal (`100.x.y.z`), tester et enregistrer.

## 3. Synchronisation Web TransitManager Désactivée pour les Commandes
**Décision :** Afin de garantir une fluidité maximale lors de l'ouverture de l'onglet Commandes et de la création de commandes, la synchronisation avec le site TransitManager a été désactivée. Toutes les commandes (simples et regroupées) sont gérées directement via la base locale ou le serveur PostgreSQL du Poste Principal.

## 4. "Perte" de l'Historique des Ventes (Filtres UI)
**Problème :** L'utilisateur ouvre l'onglet "Historique des ventes" et voit une liste vide, pensant avoir perdu toutes ses ventes passées.
**Cause :** Le `HistoryViewModel` filtre par défaut les ventes sur `DateTime.Today` (Aujourd'hui). Si aucune vente n'a été faite le jour de l'installation de la mise à jour, la liste est logiquement vide.
**Solution :** Dire à l'utilisateur de changer le filtre de date sur "Ce mois" ou "Cette année" pour voir réapparaître ses milliers de ventes.

## 5. Résilience de l'Installateur (Inno Setup)
**Règle :** L'installateur ne supprime jamais les bases de données `.db` ni le dossier `C:\ProgramData\SystemeCaisse` contenant `poste.json`, les sauvegardes et le cache d'images. C'est une sécurité vitale qui empêche l'écrasement accidentel des données lors des mises à jour.

## 6. Refonte Multi-Poste (PostgreSQL + SQLite) et Unification des Fabriques
**Problème :** Lors du passage au moteur PostgreSQL, l'erreur `42P01: relation "Produits" does not exist` apparaissait et les onglets Produits, Stocks et Ventes étaient vides. Par ailleurs, un bug dans `LocalSettingsStore` rétrogradait automatiquement PostgreSQL vers SQLite au redémarrage lorsque `Host == "localhost"` sur le Poste Principal.
**Cause :** 
- L'injection de dépendances dans `App.xaml.cs` enregistrait deux fabriques en parallèle.
- `LocalSettingsStore.Load()` contenait une règle obsolète qui forçait `Provider = Sqlite` dès que `Host` valait `localhost`.
**Solution :**
- `CaisseDbContextFactory` est l'unique source de vérité pour toute l'application.
- Suppression de la rétrogradation forcée dans `LocalSettingsStore` : un Poste Principal configuré en PostgreSQL sur `localhost` conserve son moteur PostgreSQL après redémarrage.
- `DatabaseBootstrapper.InitializeAsync()` crée automatiquement la base `caisse` si elle n'existe pas (`EnsurePostgresDatabaseExistsAsync`), applique les migrations avec verrou consultatif PostgreSQL (`pg_advisory_lock`), vérifie toutes les colonnes récentes (`PreparePostgresAsync`) et importe automatiquement les données + images de `caisse.db` si la base PostgreSQL est vide.

## 7. Fluidité de Navigation des Onglets et Réactivité Caisse / Commandes
**Problème :** Le passage entre les onglets ou la validation d'un panier présentait des ralentissements, et la saisie dans la barre de recherche manquait de fluidité.
**Solution :**
- Implémentation de `TabContent.IsCached="True"` sur le `TabControl` principal pour conserver l'arborescence visuelle en mémoire (commutation 0 ms).
- Pré-filtrage en mémoire des produits actifs (`Actif == true`), désactivation de l'auto-complétion intrusive (`IsTextSearchEnabled="False"`), et élargissement du menu déroulant (`MaxDropDownHeight="420"`, `MinWidth="680"`).
- Dans `Checkout`, la remise à zéro du panier et la reprise de main par le caissier sont immédiates ; le rechargement des statistiques et des Top Produits s'effectue de manière asynchrone en arrière-plan sans bloquer l'interface.

## 8. Déploiement Production Multi-Postes avec PostgreSQL & Tailscale Autonome
**Objectif :** Permettre d'installer l'application sur un **Poste Principal** qui configure de manière 100 % autonome PostgreSQL et le réseau Tailscale, et d'y connecter facilement des **Postes Secondaires**.
**Solution mise en place (`PostgresProvisioningService`) :**
- **Sur le Poste Principal** (via l'option d'installation Inno Setup `--setup-principal` ou le bouton **⚡ Configurer automatiquement ce Poste Principal** dans *Paramètres > Système*) :
  - Détection/démarrage du service Windows `Tailscale` en mode Automatique et affichage de l'IP Tailscale (`100.x.y.z`).
  - Détection ou installation silencieuse de PostgreSQL, configuration de `postgresql.conf` (`listen_addresses = '*'`, `tcp_keepalives`) et de `pg_hba.conf` (autorisation du sous-réseau Tailscale `100.64.0.0/10` et du LAN en `md5`).
  - Ouverture automatique des ports TCP `5432` et `5433` dans le Pare-feu Windows et configuration du service PostgreSQL en démarrage Automatique avec redémarrage sur erreur.
  - Création autonome du rôle `caisse`, de la base `caisse`, de toutes les tables et migration automatique des données SQLite locales (`caisse.db`) et des photos produits (`ImagesStockees`).
- **Sur les Postes Secondaires** :
  - Saisie de l'IP Tailscale du Poste Principal, détection automatique du port (`5432` ou `5433`), synchronisation temps réel (toutes les 5 secondes) du catalogue et des images, purge automatique des connexions TCP fantômes (`NpgsqlConnection.ClearAllPools()`) en cas de micro-coupure VPN et file d'attente hors-ligne (`OfflineQueueService`) pour ne jamais bloquer l'encaissement.
