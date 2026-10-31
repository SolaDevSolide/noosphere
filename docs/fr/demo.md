# Boîte à outils de démo

[English](../en/demo.md)

Avec `NOOSPHERE_DEMO=true`, Noosphere ajoute une boîte à outils pour montrer seul ses fonctions collaboratives :
changer de personnage d'un clic et faire agir « l'autre côté » (un collègue, une autre équipe, un validateur, un
agent IA, l'invitée). Hors mode démo, toutes les adresses `/api/demo/*` répondent 404. N'activez jamais le mode démo
sur un serveur partagé : n'importe qui peut s'y connecter avec n'importe quel compte de démo.

Dans l'interface, l'en-tête porte un repère **Démo** et un bouton **Démo** ouvre la boîte à outils : personnages,
scénarios ci-dessous, agent de démo et réinitialisation, chacun avec un avis et un lien **Voir**. Elle est masquée
hors du mode démo. Voir le [guide utilisateur](guide-utilisateur.md#boîte-à-outils-de-démo).

## Personnages

| Personnage | Rôle | À essayer |
| ---------- | ---- | --------- |
| `alice` | admin | Ouvrir la console des agents, changer le plafond, essayer l'arrêt d'urgence et lire le journal des accès. |
| `bob` | membre, Team Finance et Team Payments | Valider les propositions de David sur Billing, modifier Billing pendant qu'un collègue la change, et connecter un agent IA. |
| `claire` | invitée | Voir le portail client en entier car il lui est partagé ; le reste n'apparaît qu'en aperçu. |
| `david` | membre, Team CRM | Proposer une modification d'une fiche de Finance, suivre sa validation, et laisser un agent rédiger un brouillon. |

`GET /api/demo/state` les renvoie (avec le texte en anglais et en français), ainsi que les scénarios et le compte
courant. `POST /api/demo/switch {"username": "bob"}` connecte ce compte sans mot de passe.

## Scénarios

Chaque réponse porte un `notice: {title, detail, link?}` dans la langue de la personne, que l'interface affiche en
notification. `link` est un chemin du frontend : `/{applications|flows|data-objects|pages|teams|domains}/{id}`,
`/review/{id}`, `/my-agents`, `/admin/access` ou `/`.

| Appel | Effet |
| ----- | ----- |
| `POST /api/demo/simulate/edit {kind, id}` | Un collègue de l'équipe propriétaire (un administrateur à défaut) publie une petite modification : une nouvelle révision à son nom. Enregistrer la version que vous aviez ouverte renvoie alors 409, et les propositions ouvertes dessus deviennent obsolètes (`outdatedProposals`). |
| `POST /api/demo/simulate/proposal {kind?, id?}` | Un membre d'une autre équipe soumet une proposition sur une fiche de votre équipe (choisie s'il n'y en a pas). |
| `POST /api/demo/simulate/decision {proposalId, decision, reason?}` | Un validateur de l'équipe propriétaire (ou un administrateur) accepte ou refuse une de vos propositions soumises ; un refus est motivé. |
| `POST /api/demo/simulate/agent {scenario}` | L'agent « Demo assistant », connecté pour vous, agit par les mêmes services que MCP : `draft` (recherche, lecture, brouillon que vous seul voyez), `submit` (pareil, puis soumis), `refused` (ouvre une fiche restreinte au-dessus du plafond, écrit un jeton : deux refus motivés), `quota` (appelle jusqu'à son quota puis se met en pause jusqu'à l'heure suivante ; la démo utilise 3 appels). La réponse liste les lignes d'activité écrites. |
| `POST /api/demo/simulate/guest-read {kind, id}` | `claire` lit la fiche : une fiche restreinte qui lui est partagée écrit une ligne au journal des accès ; une fiche qui ne lui est pas partagée est refusée et rien n'est journalisé. |
| `POST /api/demo/reset {"confirm": true}` | Recharge le paysage, les pages, l'historique et les propositions de démo, et supprime les connexions d'agents et le journal des accès. Les comptes, leurs mots de passe et leurs sessions restent. |

La ligne de commande `noosphere demo reset --yes` recharge aussi la démo, et réinitialise en plus les comptes de démo.
