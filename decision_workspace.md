# Choix interactifs — guide de réglage et de test

Les chemins ci-dessous partent du projet Unity `Dominion/`.

## Un prefab par structure, pas par libellé

Tous ces prefabs sont dans `Assets/Resources/UI/Choices/`. Les fenêtres détaillées sont indépendantes : modifier leurs ancres, tailles ou grilles ne modifie pas les autres familles. Elles utilisent le même script de comportement `DecisionWorkspaceView` et les mêmes vignettes, remplaçables via ses champs Card Prefab et Option Prefab.

| Demande | Prefab à modifier |
|---|---|
| Choix immédiat : une option, ou une action explicite sur une seule carte hors plateau | `DecisionQuickChoice.prefab` |
| Sélectionner une ou plusieurs cartes de la main | `DecisionInstructionBar.prefab` + cartes de la main existantes |
| Sélectionner des cartes de la défausse, des cartes regardées, de l'Écart… | `DecisionCardSelection.prefab` |
| Tout défausser ou ordonner sur le deck (Alchimiste, Fièvre) | `DecisionCardDestinations.prefab` |
| Autres options sur les cartes montrées | `DecisionCardSelection.prefab` |
| Choisir une seule option sans cartes concernées | `DecisionCardSelection.prefab` |
| Choisir plusieurs options, avec ou sans cartes concernées | `DecisionCardSelection.prefab` |
| Choisir l'ordre des cartes à replacer sur le deck | `DecisionCardDestinations.prefab` |
| Choisir une pile dans la Réserve | `DecisionInstructionBar.prefab` + piles existantes |
| Choisir une position dans le deck | `DeckPositionDecision.prefab` dans `PendingDecisionPanel.prefab` |
| Nommer une carte | `CardNameDecision.prefab` dans `PendingDecisionPanel.prefab` |

`DecisionPresentation.WorkspacePrefab()` choisit la famille à partir de l'opération et des données de décision, jamais du texte de la consigne. Le contrôleur charge chaque fenêtre à la demande et masque/vide la précédente. Les tailles ne sont pas réécrites en C#.

Trois structures sont conservées : le petit panneau de réponse immédiate, le panneau générique et le panneau de destinations. Les titres, options, compteurs et consignes viennent du contexte ; les choix simples et multiples partagent le même prefab. Dans le tableau de réglages ci-dessous, utilisez le prefab de la famille souhaitée. Le nombre de cartes sélectionnables reste une règle de jeu, pas une valeur à changer dans un prefab.

Les anciens modèles inutilisés `DecisionWorkspace`, `DecisionHandSelection` et `DecisionSupplyChoice` ont été supprimés. La main et la Réserve partagent un seul petit bandeau en haut, sans panneau latéral de source. La consigne conserve les contraintes de l’effet.

## Comportement

- Main / Réserve : cliquez directement sur les cartes candidates du plateau, puis validez dans le bandeau. Les autres choix utilisent leur fenêtre dédiée.
- Dans les fenêtres détaillées, l'origine de l'effet reste visible : priorité à l'Artefact ou la Réaction qui écoute l'événement, sinon à la carte source.
- Clic sur une carte : ajout/retrait de la sélection. À une seule sélection, une nouvelle carte remplace l'ancienne.
- Glisser une carte vers la destination : sélection. La ramener dans les cartes disponibles : désélection.
- Le survol d'une destination valide la met en évidence ; un dépôt invalide ramène la carte à son emplacement.
- Les déplacements sont une prévisualisation locale : rien n'est déplacé dans l'état du jeu avant Confirmer.
- Réinitialiser vide le brouillon de la décision actuelle, pas les décisions déjà validées.
- Confirmer devient Passer quand zéro sélection est autorisé ; un choix obligatoire ne peut pas être ignoré.
- Avec une seule carte concernée et une seule option autorisée, on peut glisser la carte sur un bouton d'option.
- Sans carte concernée, les options occupent toute la largeur disponible.
- La main et les piles de la Réserve restent visibles et sélectionnables sur le plateau, avec les surbrillances existantes.
- La recherche d'un nom de carte et le curseur de position dans le deck conservent leurs contrôles spécialisés.
- Le menu Échap reste indépendant. Les anciennes interactions du plateau restent bloquées.

## Où modifier quoi ?

| Je veux changer… | Prefab / emplacement |
|---|---|
| Dimensions et position de la fenêtre | `Assets/Resources/UI/Choices/DecisionCardSelection.prefab` → `Panel` → RectTransform |
| Assombrissement du plateau | `DecisionCardSelection` → Image → couleur / alpha |
| Taille de la consigne | `Panel/Prompt` → Text |
| Image, nom et règles de la source | `Panel/Source/Artwork`, `Name`, `Rules`, `Context` |
| Taille des cartes disponibles | `Panel/Available/Scroll/Viewport/Content` → GridLayoutGroup → Cell Size |
| Taille des cartes sélectionnées | `Panel/Chosen/Scroll/Viewport/Content` → GridLayoutGroup → Cell Size |
| Espacement des cartes | Ces mêmes GridLayoutGroup → Spacing / Padding |
| Largeur des deux zones | `Panel/Available` et `Panel/Chosen` → RectTransform |
| Couleur d'une destination survolée | `Panel/Available` / `Panel/Chosen` → DecisionDropZone → Hover Color |
| Options accompagnées de cartes | `Panel/Options/Scroll/Viewport/Content` → GridLayoutGroup |
| Options sans cartes | `Panel/OptionsOnly/Scroll/Viewport/Content` → GridLayoutGroup |
| Apparence d'une carte interactive | `DecisionWorkspaceCard.prefab` → Artwork, Label, Selected |
| Apparence d'une option | `DecisionWorkspaceOption.prefab` → Image, Button, Label, Selected, DecisionDropZone |
| Marque de sélection | Les enfants `Selected` des deux prefabs précédents |
| Boutons de validation et remise à zéro | `DecisionCardSelection.prefab` → `Panel/Confirm`, `Panel/Reset` |
| Position et taille du bandeau main / Réserve | `DecisionInstructionBar.prefab` → RectTransform racine |
| Consigne, compteur et validation du bandeau | `DecisionInstructionBar.prefab` → Prompt, Count, ConfirmDecision |

Les grilles, ScrollRect, masques, textes et boutons sont enregistrés dans les prefabs ; le C# ne reconstruit pas leur mise en page et ne réécrit pas leur Cell Size.

## Responsabilités du code

Tous les scripts ci-dessous sont dans `Assets/Scripts/HUD/`.

| Fichier | Fonction |
|---|---|
| `PendingDecisionController.cs` | Oriente vers le bon affichage et soumet par les commandes réseau existantes. |
| `DecisionWorkspaceView.cs` | Lie les données aux prefabs, affiche les candidats et les options, gère le brouillon et les validations d'interaction. |
| `DecisionDragCard.cs` | Déplacement temporaire d'une vignette ; restauration en cas de dépôt invalide ou interruption. |
| `DecisionDropZone.cs` | Vérifie le dépôt et met en évidence une destination valide. |
| `DecisionSourceView.cs` | Affiche la carte ou l'Artefact à l'origine de l'effet. |
| `DecisionPresentation.cs` | Libellés de destination et compteur ; ne déduit jamais une règle depuis le texte français. |

Une destination n'est nommée Écart, Défausse ou Deck que si l'opération est reconnue. Sinon elle reste « Sélection », sans promettre un déplacement que le moteur ne permet pas.

## Destinations et ordre du deck

Alchimiste / Fièvre : le panneau présente les cartes concernées, une zone Défausse et une zone Deck. Glisser une carte dans Défausse déplace visuellement tout le groupe. Glisser les cartes dans Deck permet de les ordonner ; toutes doivent y figurer avant validation. Glisser une carte vers la gauche ou la droite de ses voisines change sa place. Le bouton Réinitialiser annule le brouillon.

**Gauche = plus bas dans le deck ; droite = dessus du deck, donc prochaine carte piochée.** La rangée du deck reste horizontale et défilable. Dimensions, ancres et grille se règlent dans `DecisionCardDestinations.prefab` ; le code ne recrée pas sa mise en page.

La reconnaissance des deux destinations repose sur deux effets déclaratifs consécutifs `move_all_ordered` depuis `inspected`, conditionnés par les deux options, et non sur le nom de la carte ou son texte. Le client envoie l’option et l’ordre complet au Master en une commande. Le Master valide les cartes, la décision et la continuation, puis publie un seul état. Une commande invalide n’est pas publiée.

Pour le Soldat, les étapes **écarter → réactions → défausser → réactions → ordonner le deck** restent distinctes. Les deux premières utilisent le panneau générique avec la destination correspondante. La dernière utilise la rangée ordonnable et une seule validation pour l’ordre complet. Cela préserve les réactions qui peuvent modifier les cartes entre deux étapes.

Une reconnexion ou une pause autoritaire peut effacer le brouillon local ; elle ne valide pas les déplacements. La décision durable reste dans l'état réseau. Seules les cartes candidates et, pour certaines options, la carte de l'événement du joueur local sont montrées ; pas de dévoilement automatique de la main adverse.

## Vérification dans Unity

Tests EditMode ajoutés : `Assets/Editor/Tests/DecisionWorkspaceTests.cs` (présentation, limites, origine de l'effet et contrats des prefabs).

À tester en Play Mode, idéalement à 1280×720 et 1920×1080 :

1. Soldat : écarter zéro, une ou deux cartes ; déplacer une carte puis la ramener ; poursuivre vers Défausse et Deck.
2. Chapelle / Cave : sélectionner directement dans la main, dépasser la limite, désélectionner et passer quand autorisé.
3. Alchimiste / Fièvre : déposer une carte dans Défausse déplace tout le groupe ; Deck exige toutes les cartes ; inverser l’ordre puis confirmer ; vérifier la prochaine pioche.
4. Mine : remplacer une sélection unique puis choisir la pile sur le plateau.
4. Sac d'ossements / Phylactère : vérifier l'Artefact source, la carte concernée et le dépôt sur l'option.
5. Pion / Intendant : vérifier les boutons larges sans carte de prévisualisation.
6. Main volumineuse : sélection sur les cartes réelles ; défausse volumineuse : molette et glisser-déposer dans la fenêtre.
7. Déposer hors zone : la carte doit revenir, sans changer la sélection.
8. Échap pendant un choix ; pause/reprise ; reconnexion ; décision d'un autre joueur.
9. Cliquer deux fois sur Confirmer : aucun second envoi ni interaction pendant l'attente.

Les validations YAML / références des prefabs peuvent être réalisées hors Unity. La compilation, les tests EditMode et le rendu Play Mode nécessitent l'éditeur Unity.

## Choix rapides

`DecisionQuickChoice.prefab` présente la source à droite, la carte concernée à gauche et les boutons de réponse. Aucune zone Sélection, aucun bouton Réinitialiser ou Confirmer. Un clic envoie directement la réponse ; les boutons sont désactivés pendant l’envoi. Échap et la pause restent disponibles.

- Vassal et cas déclaratifs équivalents : **Jouer / Laisser en défausse**. La carte a déjà été défaussée avant la décision : le second bouton la laisse à sa place.
- Une seule carte candidate hors main/Réserve, suivie de `play_selected`, `trash_selected` ou `discard_selected` : bouton portant l’action, avec Passer quand autorisé.
- Choix d’une option unique, jusqu’à quatre réponses et au plus une carte concernée : boutons directs avec leurs libellés déclaratifs.
- Plusieurs cartes, plusieurs options à sélectionner, recherche de nom, position dans le deck et ordre du deck : conserver les contrôles spécialisés.
- Main / Réserve : conserver le bandeau et la sélection sur le plateau.

Routage : `DecisionPresentation.IsQuickChoice`. Comportement : `DecisionQuickChoiceView`. Dimensions et disposition : prefab uniquement. Pour tester : Vassal jouer/refuser, choix d’option obligatoire/facultatif, double clic, pause/reprise et passage à un choix multiple.
