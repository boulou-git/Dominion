# Où trouver les UI ?

Tous les chemins partent de `Dominion/Assets/Resources/UI/`.

| Dossier | Éléments |
|---|---|
| Choices | `CardNameDecision`, `DecisionCardDrawer`, `DecisionQuickChoice`, `DecisionCardDestinations`, `DecisionDeckOrderCompact`, `DecisionInstructionBar`, `DecisionOption`, `DecisionSourceContext`, `DecisionWorkspaceCard`, `DecisionWorkspaceOption`, `DeckPositionDecision`, `PendingDecisionPanel` |
| Cards | `CardBackReference`, `CardCostOverlay`, `CardZoomOverlay`, `RuntimeCard`, `SupplyCard`, `VictoryPointShield` |
| Connection | `ConnectionScreen`, `StartupSplash` |
| Lobby | `CardSelectionTile`, `ExtensionTile`, `LobbyReadyPlayerRow`, `LobbyRevealControls`, `LobbySetupScreen` |
| EndGame | `EndGameFlow`, `EndGameRankingRow`, `EndGameRankingStage`, `EndGameScoreRow`, `EndGameScoringStage` |
| Board | `ArtifactTile`, `GamePauseMenu`, `GameScreen`, `PlayerBoardTab`, `ReserveExtrasUi`, `SpecialPileTile`, `TrashPileUi` |

Les fichiers `.meta` des assets déplacés sont conservés : les références Unity par GUID restent valides. Les chemins Resources.Load, les outils de création et les tests utilisent les nouveaux dossiers.

Les modèles inutilisés `DecisionWorkspace`, `DecisionHandSelection` et `DecisionSupplyChoice` ont été supprimés après vérification des références par GUID et des chargements par chemin. Les panneaux auxiliaires de choix sont conservés car le contrôleur les charge encore.

Pour la main et la Réserve, modifier `Choices/DecisionInstructionBar.prefab`. Pour les choix détaillés, voir [le guide des choix](decision_workspace.md).

Tous les choix génériques utilisent désormais `DecisionQuickChoice`, y compris les sélections multiples. `DecisionCardSelection` a été supprimé. `DecisionCardDestinations` gère les répartitions entre plusieurs zones ; `DecisionDeckOrderCompact` est réservé au simple ordre du deck.

## Propriétaires uniques

| Fonction | Propriétaire | Règle |
|---|---|---|
| Écran de jeu | `Board/GameScreen.prefab` + `EditableGameBootstrap` | Une seule instance `DominionGameUI` ; l'ancien `GameHUD` de la scène est désactivé puis supprimé au lancement. |
| Lobby | `Lobby/LobbySetupScreen.prefab` + `EditableLobbyBootstrap` | Une seule instance de l'écran de connexion et de l'écran de préparation ; l'ancien `Canvas` est désactivé puis supprimé. |
| Zoom de carte en jeu | `GameScreenController.ShowCardZoom` | Le Journal, la Réserve, l'Achat et l'Écart délèguent tous à ce même overlay. |
| Menu pause | `Board/GamePauseMenu.prefab` | Une seule instance `DominionPauseMenu` par scène de jeu. |
| Fin de partie | `EndGame/EndGameFlow.prefab` | Une seule instance `DominionEndGameFlow` par scène de jeu. |
| EventSystem | `SingleEventSystemGuard` | Un seul EventSystem actif, y compris pendant les transitions additives. |

`FollowActivePlayerToggle`, `JournalEntry` et `KingdomRevealScreen` n'existent plus comme prefabs autonomes : ces éléments sont déjà composés dans leur écran parent. La sensibilité des listes du lobby est également sérialisée directement dans `LobbySetupScreen.prefab`, sans objet runtime auxiliaire.
