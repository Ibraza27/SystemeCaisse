# PROJECT_GUIDELINES.md

Ce document recense les règles d'or et les optimisations critiques du projet **SystemeCaisse** pour garantir la stabilité, la fluidité et la scalabilité de l'application. Il sert de référence pour toutes les futures implémentations.

## 1. Stratégie Multi-Écran (Admin vs Client)

L'application est conçue pour fonctionner avec deux écrans (un pour le vendeur, un pour le client).

### Règles d'or :
- **Détection Automatique** : La fenêtre client (`CustomerDisplayWindow`) ne doit s'ouvrir QUE si au moins deux écrans sont détectés (`screens.Count < 2 → return`).
- **Positionnement** : 
  - Par défaut, la fenêtre Admin (`MainWindow`) s'affiche sur l'écran principal.
  - La fenêtre Client s'affiche sur le premier écran secondaire détecté.
  - Si l'utilisateur change la position dans les réglages, l'application doit immédiatement inverser les positions de TOUTES les fenêtres ouvertes.
- **Logique de Centrage** : Toutes les fenêtres modales (Remises, Poids, Sélection produit) doivent être centrées sur l'écran de l'Admin via la méthode `SetupWindowOwner(win)`.
- **Logging** : Le fichier `startup_log_v2.txt` trace la détection des écrans et l'ouverture de la fenêtre client pour le diagnostic.

## 2. Gestion des Fenêtres Modales et Alertes

**TOUTES** les alertes (`MessageBox`) et fenêtres secondaires doivent être ancrées à la fenêtre principale pour éviter qu'elles n'apparaissent sur l'écran client ou derrière d'autres fenêtres.

### Règles strictes :
- **MessageBox** : TOUJOURS spécifier `Application.Current.MainWindow` comme premier paramètre (owner).
  ```csharp
  // ✅ CORRECT
  MessageBox.Show(Application.Current.MainWindow, "Message", "Titre", ...);
  // ❌ INTERDIT — s'affiche potentiellement sur l'écran client
  MessageBox.Show("Message", "Titre", ...);
  ```
- **Fenêtres personnalisées (ShowDialog)** : TOUJOURS appeler `SetupWindowOwner(dialog)` (dans MainViewModel) ou assigner `win.Owner = Application.Current.MainWindow` avant `ShowDialog()`.
- **Fichiers concernés** : MainViewModel, PromotionsViewModel, InventoryViewModel, HistoryViewModel, SettingsViewModel, StocksViewModel, ProductsViewModel, et toutes les fenêtres XAML code-behind.

## 3. Performance et Stabilité Graphique

- **Rendu Logiciel (Software Rendering)** : Pour éviter les deadlocks GPU ou les gels d'interface fréquents en WPF sur certaines configurations, le rendu logiciel est forcé globalement au démarrage dans `App.xaml.cs`.
  ```csharp
  RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
  ```

## 4. Intégrité des Données et Panier

- **Mise en Attente (Suspending Sales)** : Lorsque l'on suspend une vente, tous les paramètres du panier doivent être sauvegardés, y compris les **remises manuelles globales du panier** (`BasketRemiseManuelle`).
- **Calcul des Totaux** : La méthode `UpdateTotal()` dans `MainViewModel` est la source unique de vérité pour le calcul du montant final. Elle prend en compte les remises par article (auto/manuelles) et la remise globale.
- **Événements Panier** : Un seul abonnement `Panier.CollectionChanged` doit exister dans le constructeur. Ne JAMAIS dupliquer ce bloc.
- **Validation Fluide (`Checkout`)** : Après l'encaissement d'un panier, la réinitialisation du panier (`ResetSale()`) est immédiate pour permettre au caissier d'encaisser le client suivant ou de taper dans la barre de recherche sans aucune latence. Le rafraîchissement des Top Produits et du Dashboard s'exécute en arrière-plan.

## 5. Synchronisation en Temps Réel

- **Paramètres Entreprise** : Toute modification des coordonnées de l'entreprise (nom, adresse, etc.) dans `SettingsViewModel` doit déclencher `ReloadEntrepriseInfo()` dans le `MainViewModel` pour rafraîchir instantanément toutes les vues (Ticket, Écran Client, etc.).
- **Auto-Scroll Client** : La liste des produits sur l'écran client doit scroller automatiquement vers le bas à chaque ajout pour que le dernier produit soit toujours visible.

## 6. Initialisation Résiliente

L'initialisation de l'application (`MainViewModel.InitializeAsync`) doit être **résiliente** : un échec d'un sous-ViewModel ne doit JAMAIS bloquer les suivants.

### Règles :
- **SafeInitAsync** : Chaque sous-VM s'initialise via `SafeInitAsync(name, func)` qui encapsule l'appel dans un try/catch individuel avec log fichier.
- **Pas de MessageBox dans les catch de background threads** : Utiliser `System.Diagnostics.Debug.WriteLine` + `System.IO.File.AppendAllText("startup_log_v2.txt", ...)` au lieu de `MessageBox.Show` qui crasherait depuis un thread de fond.
- **Ordre d'initialisation** : ProductsVM → StocksVM → HistoryVM → SettingsVM → DashboardVM → PromotionsVM → InventoryVM → AnalysisVM.

## 7. Promotions

- **Date de fin** : Une promotion peut avoir une date de fin **indéterminée** (`DateFin = null`). Dans ce cas, elle reste active tant qu'elle n'est pas désactivée manuellement.
- **Validation** : La logique `ApplyAutomaticPromotions` doit traiter `DateFin == null` comme "toujours valide" (`p.DateFin == null || p.DateFin >= DateTime.Today`).
- **Anti-double promotion par unité** : Un même **article (par ID produit)** ne peut PAS recevoir de double remise sur la **même unité**. Si un article a une quantité de 3 et qu'une promo en consomme 2, l'unité restante reste disponible pour d'autres promotions.
  - **Mécanisme** : `ApplyAutomaticPromotions()` utilise un `Dictionary<CartItemViewModel, decimal>` (`consumedQty`) pour suivre combien d'unités de chaque ligne ont été consommées par les promos. Les helpers `GetAvailableQty()` et `ConsumeQty()` gèrent le suivi.
  - **Multi-label** : Si une ligne panier bénéficie de plusieurs promos (sur des unités différentes), les noms de promos sont concaténés avec ` + ` via `AppendPromoLabel()`.
  - **Exclusion** : Les promotions de type `remise_total` et `seuil_panier` (remises globales sur le panier) ne sont PAS concernées par cette règle et s'appliquent toujours en complément.

## 8. Modes de Paiement Caisse et Impression Noir & Blanc

- **CB par défaut** : Le mode de paiement par défaut en Caisse est **CB** (Carte Bancaire).
  - `_selectedPaiementMode` est initialisé à `"CB"`.
  - `ResetSale()` remet le mode à `"CB"` après chaque validation de vente.
- **Modes supportés en Caisse** : `CB`, `Espèces`, `Wero`, `En ligne`, et `Mixte` (combinable). Tous les moyens utilisés apparaissent clairement sur le ticket de caisse.
- **Logo Ticket Noir & Blanc** : Lors de l'impression d'un ticket de caisse ou d'un ticket de commande, `PrintService.LoadMonochromeLogo()` convertit automatiquement le logo en niveaux de gris / noir et blanc contrasté (`FormatConvertedBitmap` `PixelFormats.Gray8`) pour garantir une impression nette sur imprimante thermique.

## 9. Paiement Espèces — Précision et Bouton Somme Exacte

### Bug corrigé :
- **Comparaison décimale** : La vérification du montant reçu utilise `Math.Round(MontantRecu, 2) < Math.Round(Total, 2)` pour éviter les faux positifs "montant insuffisant" causés par des erreurs de précision décimale (ex: `1000.00` vs `999.9999999...`).

### Bouton "= Exact" :
- Un bouton **"= Exact"** est affiché à côté du champ "Reçu" dans le TicketView lorsque le mode de paiement est Espèces ou Mixte.
- Au clic, il remplit automatiquement le champ `MontantRecu` avec la valeur exacte de `Total` via `FillExactAmountCommand`.

## 10. Remise "Prix de Vente" (Price Override)

Permet de modifier le prix de vente d'un article **dans le panier courant uniquement**, sans impacter le prix initial du produit dans la base de données.

### Fonctionnement :
- Accessible depuis le bouton **REMISE** → **"💰 Prix de vente"** dans `ManualDiscountSelectionWindow`.
- L'utilisateur sélectionne un article du panier, puis saisit le **nouveau prix de vente** souhaité.
- La remise est calculée comme `(PrixOriginal - NouveauPrix) × Quantité` et stockée dans `RemiseManuelleFixed`.
- La propriété `PriceOverridePerUnit` dans `CartItemViewModel` conserve la différence par unité, ce qui permet de **recalculer automatiquement** la remise lorsque la quantité change.

## 11. Hygiène du Code, Sécurité et Dépôt Git

### Fichiers interdits dans le dépôt Git :
- Aucun `.exe`, `.dll`, `.pdb`, `.zip`
- Aucun fichier `.log`, `.txt` de debug (`startup_log_v2.txt`, `build.log`, etc.)
- Aucun fichier `.db` (base de données SQLite) ou `.caisse-backup`
- Aucun fichier de configuration local (`poste.json`, `network_db.json`, `network_db_config.json`)
- Aucun certificat privé (`.pfx`, `.cer`)
- Aucune adresse IP Tailscale réelle ou clé API en dur dans le code ou la documentation (utiliser toujours `100.x.y.z` dans la documentation)
- Aucun dossier `publish/`, `Distribution/`, `BuildOutput/`, `SAVE/`, `.vs/`

### Règles de code :
- **Pas de `MessageBox.Show` de debug** en production. Utiliser `Debug.WriteLine` ou `AppLog`.
- **Pas de code dupliqué**. Si un bloc est copié-collé, le refactoriser en méthode.
- **Pas de `catch { }` vide** sans raison justifiée. Toujours logger l'erreur au minimum.

## 12. Déploiement et Sécurité (Signature & Inno Setup)

Pour garantir un déploiement fluide et professionnel sans blocages "Éditeur inconnu" ou "Smart App Control" :
- Le script `BuildAndPackage.ps1` compile l'application en mode `Release` `win-x64` `--self-contained true`, signe les binaires `SystemeCaisse*` avec le certificat local, génère l'installateur `Distribution\Setup_SystemeCaisse.exe` via Inno Setup (`systemecaisse.iss`), et exporte `SystemeCaisseCert.cer` + `InstallerAutorisationSecurite.bat` dans `Distribution\`.
- L'installateur `systemecaisse.iss` propose une tâche optionnelle : **"Configurer ce poste en tant que Poste Principal (Serveur PostgreSQL + Tailscale autonome)"** qui lance l'application avec `--setup-principal`.

## 13. Intégration Balance RS-232 (Adam Equipment Swift SWZ)

L'application supporte une balance connectée via un adaptateur USB-RS232 (FTDI FT232R) pour la pesée automatique des produits.

### Architecture :
- **Service** : `SerialScaleService.cs` (thread dédié haute priorité avec boucle de lecture serrée)
- **UI** : `WeightInputWindow` avec double mode Automatique (balance) / Manuel (clavier)
- **Configuration** : Section "Balance (RS-232)" dans l'onglet Périphériques des paramètres
- **Initialisation** : Le service est démarré dans `MainViewModel.InitializeAsync()` si activé en config

## 14. Gestion des Images Produits (Local & Multi-Postes PostgreSQL)

### Règles :
- **Stockage local** : Dossier `Images/Produits/` à la racine de l'exécutable et cache local dans `C:\ProgramData\SystemeCaisse\cache\images\`.
- **Stockage Multi-Postes PostgreSQL** : Table `ImagesStockees` gérée par `ImageStorageService`. Lorsqu'une image produit ou un logo est ajouté/modifié sur un poste, il est envoyé dans PostgreSQL (`UpsertAsync`), la version `DataDomains.Images` est incrémentée, et tous les autres postes téléchargent automatiquement l'image dans leur cache local (`SyncCacheAsync`).
- **Nommage** : `produit_{Id}_{ticks}.png` — le timestamp force le rechargement du cache WPF.
- **Affichage** : Les images apparaissent dans : Panier admin, Panier client, Fenêtre poids, Remise article, Promotions, Écran client, Dashboard, Stocks, Commandes.

## 15. Intégration TPE Verifone (USB/Série)

L'application supporte un Terminal de Paiement Électronique (TPE) Verifone connecté via USB (port COM virtuel) pour envoyer le montant CB au terminal et recevoir un statut (Accepté/Refusé).
**IMPORTANT** : L'application n'a JAMAIS accès aux données de carte bancaire. Elle envoie uniquement le montant et reçoit un statut.

## 16. Module Commandes (Simples & Regroupées)

L'onglet **📋 Commandes** (index 2, entre Caisse et Produits) permet de gérer des commandes clients avec paiement partiel, livraison optionnelle, promotions et commandes regroupées multi-clients.

### Architecture & Ergonomie :
- **Barre de recherche Caisse & Commandes** :
  - Produits inactifs strictement exclus (`Actif == true`).
  - Auto-complétion native désactivée (`IsTextSearchEnabled="False"`) pour une saisie fluide et rapide.
  - Liste déroulante élargie (`MaxDropDownHeight="420"`, `MinWidth="680"`) pour une meilleure lisibilité.
- **Types de paiement Commandes (sur une seule ligne compacte)** :
  - `en_ligne` (🌐 En ligne — par défaut), `cb` (💳 CB), `espece` (💵 Espèce), `virement` (🏦 Virement), `wero` (📱 Wero), `sur_place` (🏪 Sur place).
- **Statuts de commande** :
  - `en_attente` (⏳ En attente), `validee` (✔️ Validée — avec bouton rapide dans le détail de commande et les actions batch), `traitee` (✅ Traitée — décrémente le stock), `annulee` (❌ Annulée).
- **Champ Commentaire** :
  - Présent dans `CommandeClientInfoWindow`, affiché dans le détail de la commande (encadré jaune) et imprimé sur le ticket de commande.
- **Recherche Villes & Codes Postaux (`CommuneService`)** :
  - Indexation normalisée en mémoire (sans accents ni tirets), permettant de chercher indifféremment par code postal, nom partiel ou combinaison des deux.
- **Corbeille des Commandes Supprimées** :
  - Bouton icône 🗑️ avec compteur à côté du bouton Actualiser permettant d'afficher les commandes mises à la corbeille, de les **Restaurer** ou de les **Supprimer définitivement**.
- **Commandes Regroupées Multi-Clients (`👥 Cmd Regroupée`)** :
  - Permet d'ajouter plusieurs clients (`Client 1`, `Client 2`, etc.) dans une même commande.
  - Chaque ligne du panier est rattachée au sous-client actif (`LigneCommande.ClientNom`).
  - Le récapitulatif des sous-totaux par client dans le panier est affiché dans une liste déroulante compacte (`MaxHeight="58"`, affichant 2 lignes visibles) pour laisser un maximum d'espace aux articles du panier, tout en affichant le **Total Global à payer** de l'ensemble des commandes regroupées.
  - La fiche client finale regroupe la livraison, le mode de paiement et le client référent, tandis que le détail et le ticket imprimé détaillent le panier et le sous-total de chaque client.
- **Ticket Récapitulatif Produits (`CommandeRecapWindow`)** :
  - Affiche des petits pointillés de guidage visuel jusqu'à la colonne quantité et précise systématiquement l'unité (`kg` ou `pce`) dans la colonne quantité.

## 17. Sauvegarde et Restauration Universelles (`.caisse-backup` & `.db`)

- **Compatibilité SQLite & PostgreSQL** : `DataTransferService` permet d'exporter et de restaurer l'intégralité de la base (produits, ventes, commandes, stocks, promotions, entreprise, configuration et images) au format `.caisse-backup` (ou d'importer un fichier SQLite `.db`), que le poste soit en mode SQLite local ou en mode PostgreSQL Multi-Postes.
- **Sauvegarde automatique** : Le Poste Principal effectue automatiquement une sauvegarde quotidienne dans `C:\ProgramData\SystemeCaisse\backups\` (conservation des 30 dernières sauvegardes).

## 18. Déploiement Production Multi-Postes (PostgreSQL + Tailscale Autonome)

L'application fonctionne en production sur une architecture **Client-Serveur PostgreSQL via Tailscale VPN**, gérée de bout en bout par `PostgresProvisioningService`, `CaisseDbContextFactory`, `DatabaseBootstrapper` et `ConnectivityMonitor`.

### 1. Rôles des Postes (`C:\ProgramData\SystemeCaisse\config\poste.json`)
| Rôle | Moteur | Hôte PostgreSQL | Description |
|------|--------|-----------------|-------------|
| **Poste Principal (Serveur)** | `PostgreSql` | `localhost` (`5432`/`5433`) | Héberge la base PostgreSQL `caisse`, accepte les connexions Tailscale & LAN, effectue les sauvegardes auto |
| **Poste Secondaire (Client)** | `PostgreSql` | IP Tailscale du Principal (`100.x.y.z`) | Se connecte au serveur PostgreSQL du Poste Principal via Tailscale |
| **Poste Autonome (Local)** | `Sqlite` | Fichier `caisse.db` local | Mode mono-poste sans serveur PostgreSQL |

### 2. Provisionnement Autonome du Poste Principal (`PostgresProvisioningService`)
Accessible lors de l'installation (`--setup-principal`) ou en 1 clic dans **Paramètres > 🔧 Système** :
1. **Service Tailscale** : Vérifie et démarre le service Windows `Tailscale` en mode `Automatic` et détecte l'IP `100.x.y.z`.
2. **Moteur PostgreSQL** : Détecte le service PostgreSQL existant ou installe PostgreSQL 16 silencieusement. Configure le service Windows en mode `Automatic` avec redémarrage automatique en cas d'arrêt (`sc.exe failure`).
3. **Configuration Réseau PostgreSQL** :
   - `postgresql.conf` : `listen_addresses = '*'`, `tcp_keepalives_idle = 30`, `tcp_keepalives_interval = 10`, `tcp_keepalives_count = 5`.
   - `pg_hba.conf` : Autorise `127.0.0.1/32` et `::1/128` (`trust`), ainsi que le sous-réseau Tailscale `100.64.0.0/10` et les sous-réseaux privés LAN (`md5`).
4. **Pare-feu Windows** : Ouvre automatiquement les ports TCP `5432` et `5433`.
5. **Base, Schéma & Migration** : Crée le rôle `caisse` et la base `caisse`, exécute `DatabaseBootstrapper.InitializeAsync()` pour créer toutes les tables/colonnes, et importe automatiquement les données de `caisse.db` + toutes les photos produits (`ImagesStockees`) si la base PostgreSQL vient d'être créée.

### 3. Connexion des Postes Secondaires & Maintien Réseau
- **Auto-détection du port** : `TestAndPrepareSecondaryConnectionAsync` teste le port configuré (`5432`) et bascule automatiquement sur `5433` si nécessaire.
- **Résilience aux micro-coupures VPN (`ConnectivityMonitor`)** :
  - Test de connectivité toutes les 10 secondes.
  - En cas de perte ou de rétablissement de la connexion, exécute `NpgsqlConnection.ClearAllPools()` pour purger immédiatement les connexions TCP mortes et relance `tailscale up --unattended` si le tunnel est tombé.
- **Mode Hors-Ligne Transparent (`OfflineQueueService` & `CatalogCacheService`)** :
  - Si le Poste Principal est temporairement injoignable, le Poste Secondaire continue d'encaisser les ventes dans sa file locale et les synchronise automatiquement dès le retour de la connexion.
- **Synchronisation Temps Réel (`DataVersionMonitor`)** :
  - Polling léger toutes les 5 secondes sur la table `DataVersions`. Tout changement de produit, stock, prix, promotion, entreprise ou image sur un poste rafraîchit automatiquement les autres postes.

## 19. Fluidité de Navigation et Maintien en Mémoire des Vues (`TabContent.IsCached`)

- **Conservation du Visual Tree** : Le `TabControl` de navigation dans `MainWindow.xaml` porte l'attribut `controls:TabContent.IsCached="True"` pour conserver chaque vue en mémoire et commuter en 0 ms.
- **Pas de chargement bloquant sur le thread UI** : Lors du changement d'onglet, aucun rechargement synchrone de données ne doit s'exécuter sur le thread UI.
