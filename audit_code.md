# Audit de maintenance — 21 septembre 2026

Base : `fleaux`, commit `41e15c9`. Première passe transversale de revue statique,
pas une certification de sécurité ni une validation exhaustive de toutes les cartes.
Les sept images modifiées localement sont conservées et exclues du commit.

## Corrections livrées

| Zone | Défaut identifié | Correction |
|---|---|---|
| Réception Photon | `FromJson` pouvait lever une exception dans le callback réseau | Rejet du JSON malformé, conservation de l'état local, avertissement |
| Charge réseau | Décompression gzip sans limite mémoire | Plafond symétrique de 8 Mio décodés ; arrêt de lecture avant dépassement ; refus à l'encodage |
| Autorité sociale | Pause vérifiée par l'UI mais pas par les méthodes Master | Validation commune du chat et des emotes : Master, partie, pause, fin, décision, epoch |
| Routeur de tours | Singleton écrasable par un second composant, référence statique non nettoyée | Refus du second routeur, nettoyage à la destruction, Update réservé au routeur enregistré |
| Prefab social | Références des quatre GameObjects d'emote avec un fileID absent | Références vers le véritable objet racine `1000`, test des références sérialisées |
| Journal | Pseudo/message pouvant introduire des balises rich text | Neutralisation des chevrons, de même longueur pour conserver les indices des liens |
| Animations | Nombre d'emotes simultanées non borné, nettoyage incomplet | Limite configurable, nettoyage à la désactivation, retrait des listeners de boutons |
| Réglages | Durée/distance d'emote en constantes C# | Champs sérialisés dans le composant du prefab |
| Tests | Assertions de collections ambiguës et sept assertions commentées | `Has.Member` / `Has.None.EqualTo`, réactivation des assertions |

La limite de 8 Mio est un garde-fou mémoire applicatif, **pas** une taille de message
garantie par Photon. Un snapshot beaucoup plus petit peut déjà être trop coûteux.

## Architecture observée et points à préserver

- `PlayersTurnsHandler` route les RPC ; `ValidateSender` compare l'identité demandée à l'émetteur Photon.
- `NetworkGameState` vérifie version/epoch, clone, applique les règles, valide le snapshot puis publie une charge binaire.
- `RoomConnectionHandler` centralise connexion, room, reconnexion et changement de Master.
- `GameStateValidator` contrôle notamment joueurs, registre des cartes, zones, piles et résolution.
- `Rules/` sépare coûts, pioche, réception, écart, durées, réactions et résolution ; les définitions de cartes restent dans les extensions.
- `EditableGameBootstrap` et les gardes de scène évitent déjà plusieurs UI concurrentes.
  Ses `AddComponent` de secours sont conditionnels : ne pas les supprimer sans tester les scènes historiques.
- Les anciens composants ne doivent pas être effacés au seul motif de leur nom : vérifier leurs GUID dans scènes/prefabs avant suppression.

## Risques encore ouverts, par priorité

| Priorité | Constat | Suite recommandée |
|---|---|---|
| Haute — sécurité | Le snapshot complet, dont mains et decks, est dans les propriétés de room accessibles aux clients | Séparer état public et données privées ; décider du modèle de confiance avant une publication compétitive |
| Haute — sécurité | `CanWrite` est une garde du client applicatif, pas une autorisation exécutée sur le serveur Photon | Ne pas présenter ce modèle comme anti-triche ; autorité serveur nécessaire face à un client modifié |
| Haute — réseau | Commit local optimiste après mise en file de `SetCustomProperties`, sans confirmation serveur de chaque état | Tester perte réseau pendant commit et migration ; définir un protocole d'acquittement/révision avant refonte |
| Moyenne — réseau | Chat et emotes clonent/republient le snapshot et incrémentent sa version | Canal social séparé avec sender vérifié, rate limit et politique explicite d'historique ; éviter de rendre périmée une commande de jeu simultanée |
| Moyenne — chat | Le champ est vidé lors de l'envoi, sans confirmation d'acceptation Master | Ajouter un identifiant de requête et un ACK/rejet ; conserver le brouillon jusqu'à acceptation, afficher le cooldown |
| Moyenne — UI | Les visuels de boutons de la roue et les prefabs d'emotes flottantes sont distincts | Partager une configuration visuelle si les icônes doivent évoluer ensemble |
| Moyenne — maintenance | `PendingDecisionController` dépasse 1 000 lignes ; `GameScreenController` dépasse 700 | Extraire présentation/sélection/soumission progressivement avec tests PlayMode ; un découpage en fichiers seul ne réduit pas les responsabilités |
| Moyenne — connexion | Nom unique de room `Dominion`, paramètres de session dans `RoomConnectionHandler` | Pour plusieurs parties simultanées, introduire un identifiant de room et une configuration explicite |
| Moyenne — performance | Clonage JSON fréquent et rafraîchissements UI complets | Profiler avec gros journal, cartes Durée et décisions multiples avant optimisation |

## Où modifier quoi

Chemins relatifs au projet Unity `Dominion/`.

| Besoin | Source à ouvrir |
|---|---|
| Composition du journal, saisie et roue | `Assets/Resources/UI/Emotes/JournalSocialPanel.prefab` |
| Durée, montée, maximum d'emotes, son optionnel | Composant `JournalSocialPanel` du même prefab : `_emoteDuration`, `_emoteRise`, `_maxConcurrentEmotes`, `_emoteSound` |
| Quatre visuels flottants | `Assets/Resources/UI/Emotes/EmoteBlue.prefab`, `EmoteGreen.prefab`, `EmoteGold.prefab`, `EmoteRose.prefab` |
| Longueur maximale, cooldown, identifiants d'emote | `Assets/Scripts/Rules/JournalRules.cs` — règles communes, pas réglages locaux indépendants |
| Couleurs/types/liens du journal | `Assets/Scripts/HUD/PublicJournalView.cs` ; contenu externe via `SafePlainText` |
| Rejet des commandes sociales pendant pause | `Assets/Scripts/Network/NetworkGameState.cs`, `ValidateSocialCommand` |
| Taille décodée maximale | `Assets/Scripts/Network/NetworkGameStatePhotonPayload.cs`, `MaxDecodedBytes` |
| Connexion, TTL, room et reprise | `Assets/Scripts/RoomConnectionHandler.cs` |
| Validité d'une commande / migration d'hôte | `PlayersTurnsHandler.ValidateSender`, puis `NetworkGameState` |
| Invariants de cartes et joueurs | `Assets/Scripts/Rules/GameStateValidator.cs` |

Les constantes de protocole et de règles ne sont pas automatiquement des mauvais
« paramètres en dur » : elles doivent rester partagées par tous les participants.
Les réglages purement visuels appartiennent aux prefabs.

## Vérification et recette obligatoire avant diffusion

Contrôles disponibles ici : revue de diff, `git diff --check`, contrôle ciblé des
références YAML d'emotes. Tests EditMode ajoutés pour les limites raw/legacy/gzip,
le JSON malformé, le texte du journal et les références sérialisées.
**Unity et le compilateur C# ne sont pas disponibles ici : ces tests ne sont pas exécutés.**

Dans Unity 6000.3.21f1 :

1. Importer puis lancer toute la suite EditMode, dont `PrefabUiContractTests`, `NetworkGameStatePhotonPayloadTests`, `JournalRulesTests` et les tests de cartes modifiés.
2. Démarrer deux clients ; jouer, acheter, terminer un tour, répondre à une attaque chez l'autre joueur.
3. Pendant une décision : vérifier que seule la réponse attendue et Échap restent possibles.
4. Envoyer chat/emote en même temps qu'une action ; vérifier la lisibilité et relever tout rejet de version.
5. Couper le réseau du joueur non-Master puis du Master ; reprendre avant/après TTL, contrôler doublons, mains, pause, epoch et tour actif.
6. Fermer volontairement la room ; vérifier que personne ne rejoint automatiquement la partie fermée.
7. Tester gros snapshot, fin de partie, entrée/sortie répétée de scène, activation/désactivation du panneau social et rafale d'emotes.

Ne pas conclure « connexion Photon validée » avant cette recette réelle.
