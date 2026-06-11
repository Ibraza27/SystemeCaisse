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

- **Mise en Attente (Suspending Sales)** : Lorsque l'on suspend une vente, tous les paramètres du panier doivent être sauvegardés, y compris les **remises manuelles globale du panier** (`BasketRemiseManuelle`).
- **Calcul des Totaux** : La méthode `UpdateTotal()` dans `MainViewModel` est la source unique de vérité pour le calcul du montant final. Elle prend en compte les remises par article (auto/manuelles) et la remise globale.
- **Événements Panier** : Un seul abonnement `Panier.CollectionChanged` doit exister dans le constructeur. Ne JAMAIS dupliquer ce bloc.

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
  - **Exemple validé** :
    - 3 CUISSES + 1 AILES → Phase 1 (`prix_degressif`) : 2 CUISSES → 45€ (1 restante) → Phase 2 (`offre_combine`) : 1 CUISSE + 1 AILES → 45€ → **Total = 90€**
    - 3 CUISSES seuls → 2 CUISSES → 45€ + 1 CUISSE au prix normal (25.90€) → **Total = 70.90€**
  - **Exclusion** : Les promotions de type `remise_total` et `seuil_panier` (remises globales sur le panier) ne sont PAS concernées par cette règle et s'appliquent toujours en complément.

## 8. Mode de Paiement par Défaut

- **CB par défaut** : Le mode de paiement par défaut est **CB** (Carte Bancaire), et non Espèces.
  - `_selectedPaiementMode` est initialisé à `"CB"`.
  - `ResetSale()` remet le mode à `"CB"` après chaque validation de vente.

## 9. Paiement Espèces — Précision et Bouton Somme Exacte

### Bug corrigé :
- **Comparaison décimale** : La vérification du montant reçu utilise `Math.Round(MontantRecu, 2) < Math.Round(Total, 2)` pour éviter les faux positifs "montant insuffisant" causés par des erreurs de précision décimale (ex: `1000.00` vs `999.9999999...`).

### Bouton "= Exact" :
- Un bouton **"= Exact"** est affiché à côté du champ "Reçu" dans le TicketView lorsque le mode de paiement est Espèces ou Mixte.
- Au clic, il remplit automatiquement le champ `MontantRecu` avec la valeur exacte de `Total` via `FillExactAmountCommand`.
- Cela permet au caissier de valider rapidement lorsque le client paie le montant exact.

## 10. Remise "Prix de Vente" (Price Override)

Permet de modifier le prix de vente d'un article **dans le panier courant uniquement**, sans impacter le prix initial du produit dans la base de données.

### Fonctionnement :
- Accessible depuis le bouton **REMISE** → **"💰 Prix de vente"** dans `ManualDiscountSelectionWindow`.
- L'utilisateur sélectionne un article du panier, puis saisit le **nouveau prix de vente** souhaité.
- La remise est calculée comme `(PrixOriginal - NouveauPrix) × Quantité` et stockée dans `RemiseManuelleFixed`.
- La propriété `PriceOverridePerUnit` dans `CartItemViewModel` conserve la différence par unité, ce qui permet de **recalculer automatiquement** la remise lorsque la quantité change.

### Règles :
- Le prix override s'applique **quelle que soit la quantité** — la remise se recalcule proportionnellement.
- Le prix initial dans l'onglet **Produits** n'est **jamais modifié**.
- Si l'utilisateur applique ensuite une remise classique (% ou €) sur le même article, le price override est automatiquement effacé (`PriceOverridePerUnit = 0`).
- Le scope `DiscountScope.PriceOverride` est distinct de `Basket` et `Item` dans l'enum `DiscountScope`.

## 11. Hygiène du Code et du Projet

### Fichiers interdits dans le dépôt :
- Aucun `.exe`, `.dll`, `.pdb` (sauf NuGet packages)
- Aucun fichier `.log`, `.txt` de debug
- Aucun fichier `.db` (base de données)
- Aucun dossier `publish_*`, `setup_*`, `temp_*`, `BuildOutput/`, `SAVE/`

### Règles de code :
- **Pas de `MessageBox.Show` de debug** en production. Utiliser `Debug.WriteLine` ou des logs fichier.
- **Pas de code dupliqué**. Si un bloc est copié-collé, le refactoriser en méthode.
- **Pas de `catch { }` vide**. Toujours logger l'erreur au minimum.

## 12. Déploiement et Sécurité (Signature)

Pour garantir un déploiement fluide et professionnel sans blocages "Éditeur inconnu" ou "Smart App Control" :

### Procédure de Confiance (Installation Initiale) :
Sur chaque nouvel ordinateur, avant de lancer l'installateur pour la première fois :
1.  **Certificat** : Récupérer le fichier `HippocampeEnterprise.cer`.
2.  **Installation** : 
    - Double-cliquer sur le fichier.
    - Choisir **Ordinateur local** (nécessite des droits admin).
    - Choisir **Placer tous les certificats dans le magasin suivant**.
    - Sélectionner **Autorités de certification racines de confiance**.
3.  **Validation** : Une fois installé, Windows reconnaîtra "Hippocampe Systeme de Caisse" comme un éditeur de confiance.

### Maintenance :
- **Signature** : Toute nouvelle version de `SystemeCaisse.UI.exe` ou de l'installateur DOIT être signée avec le certificat `.pfx` correspondant en utilisant l'outil `signtool.exe`.
- **Identité** : Ne jamais modifier les métadonnées d'assemblage (Company, Product) sans mettre à jour le certificat, sous peine d'invalidation de la signature.

## 13. Intégration Balance RS-232 (Adam Equipment Swift SWZ)

L'application supporte une balance connectée via un adaptateur USB-RS232 (FTDI FT232R) pour la pesée automatique des produits.

### Architecture :
- **Service** : `SerialScaleService.cs` (thread dédié haute priorité avec boucle de lecture serrée)
- **UI** : `WeightInputWindow` avec double mode Automatique (balance) / Manuel (clavier)
- **Configuration** : Section "Balance (RS-232)" dans l'onglet Périphériques des paramètres
- **Initialisation** : Le service est démarré dans `MainViewModel.InitializeAsync()` si activé en config

### Configuration matérielle optimale :

| Composant | Paramètre | Valeur optimale |
|-----------|-----------|-----------------|
| **Balance** | Mode RS-232 | `PC` (Continuous to PC) |
| **Balance** | Format | `Format 3` (poids seul : `+ 0.200kg`) |
| **Balance** | Baud Rate | `115200` |
| **FTDI (Gestionnaire périph.)** | Latency Timer | `1 ms` |
| **FTDI** | Réception (Octets) | `64` (minimum) |
| **FTDI** | Transmission (Octets) | `64` (minimum) |
| **Application** | Baud Rate | `115200` (doit correspondre à la balance) |

### Règles de code :
- **Thread dédié** : Ne JAMAIS utiliser `DataReceived` event de SerialPort (latence ThreadPool ~15ms). Toujours utiliser un thread dédié avec `ThreadPriority.Highest`.
- **Regex compilé** : Le pattern de parsing du poids doit être `static readonly` avec `RegexOptions.Compiled`.
- **Dernière ligne seulement** : En mode continu, ne traiter que la DERNIÈRE ligne reçue pour éviter la latence cumulée.
- **Dispatcher Send** : Les mises à jour UI depuis le thread série doivent utiliser `DispatcherPriority.Send` (priorité maximale).
- **Brushes frozen** : Tous les `SolidColorBrush` dans `WeightInputWindow` doivent être `static readonly` et `.Freeze()` pour le cross-thread.
- **Test connexion** : `TestConnection()` doit vérifier si le service actif utilise déjà le port (sinon erreur "port occupé").
- **Redémarrage** : Toute modification des paramètres balance doit proposer un redémarrage automatique après sauvegarde.

### Commandes RS-232 Adam Equipment SWZ :
| Commande | Format | Description |
|----------|--------|-------------|
| **Tare** | `T\r\n` | Tarer la balance |
| **Zéro** | `Z\r\n` | Remettre à zéro |
| **Print** | `P\r\n` | Demander le poids |
| **Prix unitaire** | `$XX.XX\r\n` | Envoyer le prix/kg à l'afficheur |

> **Important** : Toutes les commandes DOIVENT être en lettres MAJUSCULES.

### Produits connectés :
- **Adaptateur** : FTDI FT232R (VID: 0403, PID: 6001)
- **Balance** : Adam Equipment Swift SWZ (série)
- **Port par défaut** : COM3
- **Trame** : 8 bits, pas de parité, 1 stop bit, pas de contrôle de flux

## 14. Gestion des Images Produits

### Règles :
- **Stockage** : Dossier `Images/Produits/` à la racine de l'exécutable.
- **Nommage** : `produit_{Id}_{ticks}.png` — le timestamp force le rechargement du cache WPF.
- **Remplacement** : Lors du remplacement d'une image, SUPPRIMER l'ancien fichier avant d'écrire le nouveau.
- **Suppression** : Quand l'image d'un produit est supprimée, le fichier physique doit aussi être supprimé.
- **Affichage** : Les images doivent apparaître dans : Panier admin, Panier client, Fenêtre poids, Remise article, Promotions, Écran client, Dashboard, Stocks.

## 15. Intégration TPE Verifone (USB/Série)

L'application supporte un Terminal de Paiement Électronique (TPE) Verifone connecté via USB (port COM virtuel) pour envoyer le montant CB au terminal et recevoir un statut (Accepté/Refusé).

**IMPORTANT** : L'application n'a JAMAIS accès aux données de carte bancaire. Elle envoie uniquement le montant et reçoit un statut.

### Architecture :
- **Service** : `VerifonePaymentTerminalService.cs` (communication série via `System.IO.Ports.SerialPort`)
- **UI** : Bouton "Payer par TPE" dans `TicketView`, visible uniquement si mode CB/Mixte + TPE connecté
- **Erreur** : `TPEPaymentErrorWindow` — modale avec Réessayer/Annuler en cas d'échec
- **Configuration** : Section "Terminal de Paiement (TPE)" dans l'onglet Périphériques des paramètres
- **Initialisation** : Le service est démarré dans `MainViewModel.InitializeAsync()` si activé en config

### Configuration :

| Paramètre | Clé Config | Valeur par défaut |
|-----------|------------|-------------------|
| **Activer TPE** | `tpe_enabled` | `False` |
| **Port COM** | `tpe_port_name` | `COM1` |
| **Baud Rate** | `tpe_baud_rate` | `9600` |
| **Timeout (sec)** | `tpe_timeout` | `60` |

### Protocole CONCERT simplifié :

| Étape | Caisse | TPE |
|-------|--------|-----|
| 1 | ENQ (0x05) | → |
| 2 | ← | ACK (0x06) |
| 3 | STX + données + ETX + LRC | → |
| 4 | ← | ACK (0x06) |
| 5 | EOT (0x04) | → |
| 6 | ← | Réponse (STX + statut + ETX + LRC) |

### Codes réponse :
| Code | Signification |
|------|---------------|
| `0` | Paiement accepté |
| `1` | Refusé par la banque |
| `2` | Carte invalide |
| `3` | Erreur communication bancaire |
| `4` | Annulé par le client |
| `5` | Code PIN incorrect |
| `7` | Carte retirée prématurément |

### Règles de code :
- **Pas de données carte** : JAMAIS de numéro de carte, date d'expiration ou CVV dans l'application.
- **Bouton "Valider" préservé** : Le bouton de validation classique reste toujours disponible pour valider sans TPE.
- **Auto-validation** : Dès que le TPE retourne un succès, le panier est validé automatiquement (appel direct à `Checkout`).
- **Retry loop** : En cas d'échec, la modale d'erreur propose "Réessayer" qui relance le paiement sans perdre le panier.
- **Timeout configurable** : Le timeout de transaction est configurable (15-120 sec) car le client peut mettre du temps à saisir son code PIN.
- **Prérequis matériel** : Le driver USB Verifone doit être installé pour que le port COM virtuel apparaisse. Le TPE doit être en mode "Caisse" (intégré).

## 16. Module Commandes

L'onglet **📋 Commandes** (index 2, entre Caisse et Produits) permet de gérer des commandes clients avec paiement partiel, livraison optionnelle et promotions.

### Architecture :
- **Entités** : `Commande` (client + montants + statuts) et `LigneCommande` (miroir de `LigneVente`)
- **ViewModel** : `CommandesViewModel` — gère la liste, les filtres, la création, et les actions CRUD. Reçoit le `MainViewModel` via `SetMainViewModel()` pour accéder aux TopProducts et au ScaleService.
- **Vues** : `CommandesView` (liste + détail), mode création intégré (catalogue + panier), `CommandeClientInfoWindow` (modale client avec TextBox+ListBox filtrable)
- **Services** : `CommuneService` (chargement communes.json), `PrintService.GenerateCommandeTicketDocument()`

### Interface Nouvelle Commande (identique à la Caisse) :
- **Barre de recherche** : ComboBox éditable avec `Loaded` handler anti-auto-sélection (même code que SalesView)
- **Top Produits** : Délégués au `MainViewModel.TopProducts` — mêmes produits, même ordre, mêmes photos
- **Produits au poids** : Ouverture de `WeightInputWindow` avec `ScaleService` partagé
- **Panier** : Colonnes Image, Produit, P.U (/u ou /kg), Qté (+/-, double-clic → `QuantityInputWindow`), Total (barré si promo), ❌
- **Remise** : Même flux complet que la Caisse — `ManualDiscountSelectionWindow` → `CartItemSelectionWindow` → `DiscountValueInputWindow`
- **Attente** : Système `SuspendedCommande` avec ComboBox + bouton 📂 dans le coin haut droit du panier (même pattern que `TicketView`/`SuspendedSale`). Le panier est vidé mais on reste en mode nouvelle commande.

### Modification de commande :
- **Édition** : La commande originale n'est PAS supprimée. L'ID est stocké dans `_editingCommandeId`. Si l'utilisateur valide, la commande est mise à jour en BDD (UPDATE). Si l'utilisateur annule ("Retour à la liste"), la commande est restaurée intacte.

### Filtres Liste :
- **Recherche** : Par nom, prénom, téléphone, numéro de commande
- **Statut** : Tous / En attente / Traitée / Annulée
- **Paiement** : Tous / Réglé / Partiel / Non réglé
- **Ville/CP** : `VilleCPFilterWindow` — affiche par défaut les villes existantes des commandes pour sélection rapide + recherche dans la base communes. Multi-sélection avec pills (tags).
- **Effacer filtres** : Remet tous les filtres à zéro
- **Récap Produits** : `CommandeRecapWindow` — agrège les articles de la liste filtrée avec impression ticket

### Index des onglets (après ajout) :
| Index | Onglet |
|-------|--------|
| 0 | Tableau de Bord |
| 1 | Caisse |
| 2 | **Commandes** |
| 3 | Produits |
| 4 | Stocks |
| 5 | Ventes |
| 6 | Analyses |
| 7 | Configuration |

### Statuts :
- **Commande** : `en_attente` (défaut), `traitee`, `annulee`
- **Paiement** (calculé) : `regle` (Restant ≤ 0), `partiel` (MontantPaye > 0), `non_regle`

### Règles de stock :
- **Création** : Le stock n'est PAS décrémenté à la création.
- **Passage en "traitée"** : Le stock EST décrémenté et un `MouvementStock` est créé.
- **Annulation** : Le stock n'est PAS restauré (à gérer manuellement si nécessaire).

### Numéro de commande :
- Format : `CMD-{YYYYMMDD}-{NNN}` (ex: `CMD-20260611-001`)
- Compteur journalier basé sur `COUNT(NumeroCommande LIKE 'CMD-{date}%') + 1`

### Ticket commande :
- Même structure que le ticket vente mais avec en haut : N° Commande, Nom, Prénom, Téléphone, Adresse (si dispo), Ville/CP
- En bas : **"RÉGLÉ"** ou **"NON RÉGLÉ — Restant : XX.XX€"** en gras

### Données villes/CP :
- Fichier `Data/communes.json` (~3 Mo) embarqué depuis `geo.api.gouv.fr`
- Chargé au démarrage par `CommuneService.Load()`
- Recherche par CP (préfixe) ou nom de ville (contient)

