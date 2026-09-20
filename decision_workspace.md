# Choix interactifs — réglages et vérification

Tous les chemins de prefab ci-dessous sont dans `Dominion/Assets/Resources/UI/Choices/`.

## Quel prefab modifier ?

| Demande | Prefab | Interaction |
|---|---|---|
| Carte(s) de la main ou pile de la Réserve | `DecisionInstructionBar` | Sélection sur le plateau et bandeau de validation |
| Option unique, Vassal et autres décisions immédiates | `DecisionQuickChoice` | Un clic sur le bouton de réponse |
| Une carte parmi plusieurs, hors plateau | `DecisionQuickChoice` | Clic direct sur la carte ; Passer si facultatif |
| Plusieurs cartes ou options | `DecisionQuickChoice` | Sélection / désélection puis un seul bouton Valider |
| Soldat : répartir entre Défausse / Deck / Écart | `DecisionCardDestinations` | Glisser-déposer, ordre du deck, une validation |
| Alchimiste / Fièvre : tout défausser ou tout replacer | `DecisionCardDestinations` | Déplacer une carte change la destination du groupe |
| Réordonner le deck | `DecisionCardDestinations` | Glisser les cartes dans la rangée puis valider |
| Nommer une carte | `CardNameDecision` dans `PendingDecisionPanel` | Recherche dédiée |
| Choisir une position d’insertion | `DeckPositionDecision` dans `PendingDecisionPanel` | Contrôle dédié |

L’ancien grand panneau `DecisionCardSelection.prefab` est supprimé. `PendingDecisionPanel` reste l’hôte des contrôles spécialisés ; ce n’est plus le panneau utilisé pour les choix génériques. Le même prefab compact gère toutes les cartes : les libellés viennent de la décision, sans créer de copie par nom de carte.

## Choix compacts

La source (carte ou Artefact) reste visible à droite. Les cartes concernées apparaissent à gauche. Sans carte concernée, la liste d’options utilise toute la hauteur disponible.

Pour le Vassal, les boutons sont **Jouer** et **Laisser en défausse** : la carte a déjà été défaussée avant cette décision. Les autres actions explicites sur une unique carte utilisent leur verbe : Jouer, Écarter ou Défausser.

Les réponses immédiates bloquent les boutons dès l’envoi. Pour plusieurs cartes/options, le compteur et le bouton Valider suivent les limites imposées par le moteur. Le bouton affiche Passer si zéro sélection est autorisé. Les cartes et options sélectionnées portent une marque visible. Les listes sont défilables, à la molette ou en faisant glisser leur contenu.

## Soldat : une seule validation

Les trois zones sont dans cet ordre : **Défausse à gauche · Deck au centre · Écart à droite**. Toutes les cartes commencent sur le deck. Leur ordre initial conserve l’ordre de pioche : **gauche = plus bas ; droite = dessus, donc prochaine carte piochée**.

Déplacez les cartes entre les trois zones et réordonnez celles du deck. Valider envoie la répartition entière au Master. Réinitialiser remet toutes les cartes au centre dans leur ordre initial. Aucun déplacement de règle n’est effectué pendant le brouillon.

Le moteur exécute le plan confirmé dans l’ordre normal : écarter, résoudre les réactions, défausser, résoudre les réactions, replacer sur le deck. Le joueur ne revalide pas les étapes de tri. Une réaction indépendante garde son propre choix : le plan est conservé dans l’état réseau puis reprend après la réponse. Il survit à la sérialisation / reconnexion. Si un effet change les candidats de manière incompatible avec le plan, le moteur demande un nouveau choix au lieu d’appliquer une instruction devenue invalide.

La reconnaissance utilise la suite déclarative `choose_cards` → `trash_selected` → `choose_cards` → `discard_selected` → `move_all_ordered`, dans la zone `inspected`. Elle ne dépend pas du nom Soldat ou du texte français. Chaque carte doit apparaître exactement une fois dans la répartition ; doublons et cartes étrangères sont refusés côté Master.

## Alchimiste, Fièvre et ordre du deck

Les cartes commencent aussi sur le deck. Déposer une carte dans Défausse y déplace tout le groupe. Revenir sur Deck y replace le groupe, que l’on peut ensuite réordonner. Une validation transmet l’option et l’ordre complet. Pour un simple réordonnancement, seule la zone Deck reste visible et occupe la largeur des destinations.

## Où modifier quoi ?

| Réglage | Emplacement |
|---|---|
| Taille / position du panneau compact | `DecisionQuickChoice` → `Panel` → RectTransform |
| Taille des cartes compactes | `Panel/Available/Scroll/Viewport/Content` → GridLayoutGroup → Cell Size |
| Boutons avec cartes concernées | `Panel/Options/Scroll/Viewport/Content` → GridLayoutGroup |
| Boutons sans aperçu | `Panel/OptionsOnly/Scroll/Viewport/Content` → GridLayoutGroup |
| Carte source et texte | `Panel/Source/Artwork`, `Name`, `Rules`, `Context` |
| Consigne | `Panel/Prompt` → Text |
| Taille / position du tri | `DecisionCardDestinations` → `Panel` → RectTransform |
| Espacement entre destinations | `Panel/Zones` → HorizontalLayoutGroup |
| Défausse / Deck / Écart | `Panel/Zones/Discard`, `Chosen`, `Available` |
| Taille des cartes du tri | Chaque zone → `Scroll/Viewport/Content` → GridLayoutGroup |
| Couleur d’une destination survolée | Chaque zone → DecisionDropZone → Hover Color |
| Validation / remise à zéro du tri | `Panel/Confirm`, `Panel/Reset` |
| Apparence d’une carte / de sa sélection | `DecisionWorkspaceCard` → Artwork, Label, Selected |
| Apparence d’un bouton / de sa sélection | `DecisionWorkspaceOption` → Image, Label, Selected |
| Bandeau main / Réserve | `DecisionInstructionBar` → RectTransform, Prompt, Count, ConfirmDecision |

Les ancres, grilles et mises en page sont enregistrées dans les prefabs. Le code ne redéfinit pas leurs dimensions. Le groupe horizontal partage la largeur entre les destinations actives. Les masques des ScrollRect doivent conserver une Image opaque avec Show Mask Graphic désactivé.

## Fichiers responsables

| Fichier | Rôle |
|---|---|
| `HUD/DecisionPresentation.cs` | Choix de la présentation selon l’opération |
| `HUD/DecisionQuickChoiceView.cs` | Boutons immédiats et sélections multiples compactes |
| `HUD/DecisionWorkspaceView.cs` | Brouillon des destinations et ordre visuel |
| `HUD/DecisionDragCard.cs`, `DecisionDropZone.cs` | Clic, glisser-déposer, survol |
| `HUD/PendingDecisionController.cs` | Liaison des vues, blocage des autres interactions et soumission |
| `Rules/InspectedSortRules.cs` | Validation de la répartition et reprise du plan après les réactions |
| `Rules/DeckChoiceRules.cs` | Validation de l’option Défausse / Deck et de l’ordre complet |
| `Rules/ResolutionQueue.cs` | Conservation du plan de tri dans l’état réseau |
| `PlayersTurnsHandler.cs`, `Network/NetworkGameState.cs` | Commande au Master, validation de version et publication atomique |

Les chemins de scripts partent de `Dominion/Assets/Scripts/`.

## Vérification dans Unity

Tests EditMode : `DecisionWorkspaceTests`, `InspectedSortRulesTests`, `FleauxFoundationRulesTests`.

1. Vassal : jouer ou laisser en défausse en un clic.
2. Choix unique avec plusieurs candidats : clic sur une carte sans confirmation supplémentaire.
3. Choix multiple : limites, désélection, Passer facultatif, compteur et validation.
4. Soldat : tout conserver, tout défausser, tout écarter, répartir, inverser le deck, revenir à l’état initial.
5. Soldat avec Fossoyeur en main : répondre à la réaction puis vérifier la fin automatique du tri ; tester également après sérialisation.
6. Alchimiste / Fièvre : déplacement de tout le groupe, inversion de l’ordre, prochaine pioche.
7. Main / Réserve, recherche par nom et insertion dans le deck : contrôles spécialisés toujours utilisables.
8. Échap, pause/reprise, changement de décision, double clic et reconnexion.
9. Résolutions 1280×720 et 1920×1080 : lisibilité, défilement des listes et du deck.

Les références et le YAML peuvent être vérifiés hors Unity. La compilation, les tests EditMode et le rendu nécessitent l’éditeur Unity.
