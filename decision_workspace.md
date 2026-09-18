# Choix interactifs — guide de réglage et de test

Les chemins ci-dessous partent du projet Unity `Dominion/`.

## Comportement

- Les choix de cartes et d'options génériques s'affichent dans `DecisionWorkspace`.
- L'origine de l'effet reste visible : priorité à l'Artefact ou la Réaction qui écoute l'événement, sinon à la carte source.
- Clic sur une carte : ajout/retrait de la sélection. À une seule sélection, une nouvelle carte remplace l'ancienne.
- Glisser une carte vers la destination : sélection. La ramener dans les cartes disponibles : désélection.
- Le survol d'une destination valide la met en évidence ; un dépôt invalide ramène la carte à son emplacement.
- Les déplacements sont une prévisualisation locale : rien n'est déplacé dans l'état du jeu avant Confirmer.
- Réinitialiser vide le brouillon de la décision actuelle, pas les décisions déjà validées.
- Confirmer devient Passer quand zéro sélection est autorisé ; un choix obligatoire ne peut pas être ignoré.
- Avec une seule carte concernée et une seule option autorisée, on peut glisser la carte sur un bouton d'option.
- Sans carte concernée, les options occupent toute la largeur disponible.
- Les piles de la Réserve restent sélectionnables sur le plateau ; un panneau latéral rappelle la source.
- La recherche d'un nom de carte et le curseur de position dans le deck conservent leurs contrôles spécialisés.
- Le menu Échap reste indépendant. Les anciennes interactions du plateau restent bloquées.

## Où modifier quoi ?

| Je veux changer… | Prefab / emplacement |
|---|---|
| Dimensions et position de la fenêtre | `Assets/Resources/UI/DecisionWorkspace.prefab` → `Panel` → RectTransform |
| Assombrissement du plateau | `DecisionWorkspace` → Image → couleur / alpha |
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
| Boutons de validation et remise à zéro | `DecisionWorkspace.prefab` → `Panel/Confirm`, `Panel/Reset` |
| Source affichée pendant un choix dans la Réserve | `DecisionSourceContext.prefab` |

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

## Important : Soldat et effets en plusieurs étapes

Le moteur conserve l'ordre **écarter → résoudre les réactions → défausser → résoudre les réactions → réordonner**. Le même espace visuel présente chaque décision successivement. Il ne s'agit pas encore d'un tri simultané dans trois piles avec une seule confirmation.

Pour remettre plusieurs cartes sur le deck, le moteur demande la prochaine carte à déplacer. Les cartes suivantes se placent au-dessus : choisir d'abord celle qui doit finir plus bas. La dernière restante est déplacée automatiquement selon les règles existantes. La consigne du Soldat précise cet ordre.

Une reconnexion ou une pause autoritaire peut effacer le brouillon local ; elle ne valide pas les déplacements. La décision durable reste dans l'état réseau. Seules les cartes candidates et, pour certaines options, la carte de l'événement du joueur local sont montrées ; pas de dévoilement automatique de la main adverse.

## Vérification dans Unity

Tests EditMode ajoutés : `Assets/Editor/Tests/DecisionWorkspaceTests.cs` (présentation, limites, origine de l'effet et contrats des prefabs).

À tester en Play Mode, idéalement à 1280×720 et 1920×1080 :

1. Soldat : écarter zéro, une ou deux cartes ; déplacer une carte puis la ramener ; poursuivre vers Défausse et Deck.
2. Chapelle / Cave : dépasser la limite, réinitialiser et passer quand autorisé.
3. Mine : remplacer une sélection unique puis choisir la pile sur le plateau.
4. Sac d'ossements / Phylactère : vérifier l'Artefact source, la carte concernée et le dépôt sur l'option.
5. Pion / Intendant : vérifier les boutons larges sans carte de prévisualisation.
6. Main ou défausse volumineuse : molette dans chaque zone, dépôt sur un espace vide et sur une autre carte.
7. Déposer hors zone : la carte doit revenir, sans changer la sélection.
8. Échap pendant un choix ; pause/reprise ; reconnexion ; décision d'un autre joueur.
9. Cliquer deux fois sur Confirmer : aucun second envoi ni interaction pendant l'attente.

Les validations YAML / références des prefabs peuvent être réalisées hors Unity. La compilation, les tests EditMode et le rendu Play Mode nécessitent l'éditeur Unity.
