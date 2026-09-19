# Où trouver les UI ?

Tous les chemins partent de `Dominion/Assets/Resources/UI/`.

| Dossier | Éléments |
|---|---|
| Choices | `CardNameDecision`, `DecisionCardDrawer`, `DecisionCardSelection`, `DecisionCardDestinations`, `DecisionInstructionBar`, `DecisionOption`, `DecisionSourceContext`, `DecisionWorkspaceCard`, `DecisionWorkspaceOption`, `DeckPositionDecision`, `PendingDecisionPanel` |
| Cards | `CardBackReference`, `CardCostOverlay`, `CardZoomOverlay`, `RuntimeCard`, `SupplyCard`, `VictoryPointShield` |
| Connection | `ConnectionScreen`, `StartupSplash` |
| Lobby | `CardSelectionTile`, `ExtensionTile`, `KingdomRevealScreen`, `LobbyReadyPlayerRow`, `LobbyRevealControls`, `LobbySetupScreen` |
| EndGame | `EndGameFlow`, `EndGameRankingRow`, `EndGameRankingStage`, `EndGameScoreRow`, `EndGameScoringStage` |
| Board | `ArtifactTile`, `FollowActivePlayerToggle`, `GamePauseMenu`, `GameScreen`, `JournalEntry`, `PlayerBoardTab`, `ReserveExtrasUi`, `SpecialPileTile`, `TrashPileUi` |

Les fichiers `.meta` des assets déplacés sont conservés : les références Unity par GUID restent valides. Les chemins Resources.Load, les outils de création et les tests utilisent les nouveaux dossiers.

Les modèles inutilisés `DecisionWorkspace`, `DecisionHandSelection` et `DecisionSupplyChoice` ont été supprimés après vérification des références par GUID et des chargements par chemin. Les panneaux auxiliaires de choix sont conservés car le contrôleur les charge encore.

Pour la main et la Réserve, modifier `Choices/DecisionInstructionBar.prefab`. Pour les choix détaillés, voir [le guide des choix](decision_workspace.md).

Les variantes identiques de choix simple, multiple et avec aperçu sont fusionnées dans `DecisionCardSelection`. `DecisionCardDestinations` conserve une structure distincte (cartes disponibles, Défausse, Deck horizontal).
