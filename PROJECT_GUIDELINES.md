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

## 8. Hygiène du Code et du Projet

### Fichiers interdits dans le dépôt :
- Aucun `.exe`, `.dll`, `.pdb` (sauf NuGet packages)
- Aucun fichier `.log`, `.txt` de debug
- Aucun fichier `.db` (base de données)
- Aucun dossier `publish_*`, `setup_*`, `temp_*`, `BuildOutput/`, `SAVE/`

### Règles de code :
- **Pas de `MessageBox.Show` de debug** en production. Utiliser `Debug.WriteLine` ou des logs fichier.
- **Pas de code dupliqué**. Si un bloc est copié-collé, le refactoriser en méthode.
- **Pas de `catch { }` vide**. Toujours logger l'erreur au minimum.

## 9. Déploiement et Sécurité (Signature)

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

## 10. Intégration Balance RS-232 (Adam Equipment Swift SWZ)

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

## 11. Gestion des Images Produits

### Règles :
- **Stockage** : Dossier `Images/Produits/` à la racine de l'exécutable.
- **Nommage** : `produit_{Id}_{ticks}.png` — le timestamp force le rechargement du cache WPF.
- **Remplacement** : Lors du remplacement d'une image, SUPPRIMER l'ancien fichier avant d'écrire le nouveau.
- **Suppression** : Quand l'image d'un produit est supprimée, le fichier physique doit aussi être supprimé.
- **Affichage** : Les images doivent apparaître dans : Panier admin, Panier client, Fenêtre poids, Remise article, Promotions, Écran client, Dashboard, Stocks.
