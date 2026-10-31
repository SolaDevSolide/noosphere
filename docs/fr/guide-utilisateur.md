# Guide utilisateur : pages, propositions, historique et réglages

[English](../en/user-guide.md)

Toute personne connectée lit l'ensemble du paysage. Tout compte membre peut proposer des modifications, et l'équipe
qui possède une fiche les valide.

## Qui publie et qui propose

| Vous êtes                                   | Sur une fiche de votre équipe   | Sur une autre fiche               |
| ------------------------------------------- | ------------------------------- | --------------------------------- |
| Administrateur                              | **Modifier** (publie)           | **Modifier** (publie)             |
| Membre de l'équipe propriétaire             | **Modifier** (publie)           | **Proposer une modification**     |
| Membre d'une autre équipe, ou sans équipe   | **Proposer une modification**   | **Proposer une modification**     |
| Invité                                      | lecture seule                   | lecture seule                     |

Une application appartient à son équipe. Un flux appartient à son équipe propriétaire, ou à défaut à l'équipe de
l'application d'où il part. Une fiche sans équipe propriétaire est validée par les administrateurs.

### Proposer une modification

1. Ouvrez une application ou un flux et choisissez **Proposer une modification**.
2. Modifiez les champs comme d'habitude, donnez un titre à la proposition et expliquez **pourquoi** dans le
   commentaire : les validateurs le lisent.
3. **Envoyer la proposition**. Les lecteurs voient la version publiée tant que l'équipe propriétaire ne l'a pas
   acceptée.

Les membres de l'équipe propriétaire peuvent aussi cocher **Proposer plutôt que publier** dans le formulaire, pour
faire relire une modification par un collègue avant sa publication.

## L'espace de validation

Le bouton **Validation** de l'en-tête indique combien de propositions vous attendent. L'espace a trois onglets :

- **À valider** : les propositions sur les fiches de vos équipes (toutes pour les administrateurs) ;
- **Mes propositions** : ce que vous avez proposé, en cours ou décidé ;
- **Décidées** : les propositions acceptées et refusées.

Une proposition montre chaque fiche modifiée **côte à côte** : la version publiée à gauche, la version proposée à
droite. Les textes Markdown longs, comme les descriptions, montrent les **modifications surlignées dans le texte** :
les mots supprimés sont barrés en rouge, les mots ajoutés surlignés en vert.

- **Accepter et publier** applique la modification et crée une nouvelle version de la fiche.
- **Refuser** demande un motif (de 3 à 500 caractères), que l'auteur voit.
- L'auteur peut **Retirer** une proposition en cours.

### Propositions dépassées

Si quelqu'un a publié une nouvelle version de la fiche après l'écriture de la proposition, celle-ci est marquée comme
dépassée et ne peut pas être acceptée en l'état. Son auteur choisit **Mettre à jour depuis la dernière version** : ses
champs modifiés sont conservés par-dessus la dernière version, et les champs qui ont aussi changé entre-temps sont
marqués **Modifié des deux côtés**.

## Pages de documentation

Ouvrez **Pages** dans l'en-tête pour lire et écrire des procédures, des décisions et des présentations. Les pages
suivent la même règle que les fiches : l'équipe propriétaire (celle de la page, sinon le propriétaire de la fiche à
laquelle elle est rattachée) publie, et les autres membres **Proposent une modification**. Tout membre peut créer une
page, rattachée à une fiche ou confiée à l'équipe de son choix. Tapez `[[` dans l'éditeur pour lier une fiche ou une
autre page ; les liens vers ce qui n'existe pas s'affichent en rouge sans bloquer la publication. Voir
[Pages](pages.md) pour le détail.

## Historique

Chaque modification publiée crée une version : un enregistrement direct, une proposition acceptée ou une restauration.
Le bouton discret **Historique** d'une fiche ouvre un panneau latéral avec ses versions, leur auteur et leur date, et
un lien vers la proposition à l'origine d'une version.

- **Comparer** : cochez deux versions puis choisissez **Comparer** pour voir les champs qui ont changé entre elles.
- **Restaurer** : choisissez **Restaurer** sur une version antérieure et confirmez. Les propriétaires la republient
  dans une nouvelle version ; les autres envoient une proposition aux propriétaires.

## Fiches restreintes, invités et notes protégées

Sous le titre de chaque fiche, une puce indique qui peut la voir : **Visible par tous**, ou **Restreinte · 2 équipes,
1 personne**. Cliquez-la pour ouvrir **Qui peut voir et modifier ?** : qui lit la fiche, qui publie et
valide ses modifications, qui peut en proposer, qui lit ses notes protégées, et quelles pages rattachées restent
publiques. Détails : [accès, notes protégées et journal des accès](acces.md).

- **Restreindre une application ou une page** : les propriétaires choisissent **Restreindre cette fiche** (ou
  **Changer qui peut la voir**) dans cette fenêtre. Choisissez des équipes et des personnes avec le champ de
  recherche ; l'équipe propriétaire et les administrateurs la voient toujours. L'aperçu dit qui verra la fiche et ce
  que verront les autres. Les pages rattachées gardent leur propre visibilité : la fenêtre liste celles qui restent
  publiques. **La rendre visible par tous** lève la restriction.
- **Partager avec un invité** : sur une fiche publique, les propriétaires nomment les invités qui peuvent l'ouvrir en
  entier. Les invités voient le plan du paysage (noms des applications et domaines) ; une fiche qui ne leur
  est pas partagée affiche un court avis à la place. Les fiches restreintes leur sont entièrement cachées, et ils ne
  lisent jamais les notes protégées.
- **Confier à une autre équipe** : ouvre le formulaire sur le champ **Équipe** (**Propriétaire** sur un flux).
  L'enregistrement confie la fiche à cette équipe ; les membres qui ne peuvent pas publier l'envoient en proposition.
- **Notes protégées** : une section des applications et des flux pour les détails d'exploitation (adresses de
  production, qui appeler, où sont les identifiants). L'équipe propriétaire, la liste d'accès et les administrateurs
  les lisent ; les autres voient un cadenas et, dans l'historique et les comparaisons, `••• protected` à la place du
  texte. Un membre qui ne peut pas les lire peut écrire des notes de remplacement ; laisser le champ vide garde les
  notes actuelles.
- **Sensibilité** : une application est aussi sensible que la donnée la plus sensible dont elle est la source, ou que
  ses flux transportent. Le badge à côté de la puce de visibilité en donne les raisons. Dans le paysage,
  **Filtrer → Applications · sensibilité au moins** estompe les applications moins sensibles, et la recherche affiche
  le niveau après chaque application.
- **Les secrets sont refusés** : coller un jeton, une clé privée ou un mot de passe dans un champ (fiches, pages,
  propositions, réglages, imports) est refusé avec un message sous le champ ; rien n'est enregistré. Écrivez plutôt
  où se trouve le secret. Les jetons de substitution comme `<password>`, `***`, `${VAR}` ou `{{var}}` sont acceptés.

### Export

**Import et export → Exporter le paysage** propose **Ce que vous voyez** (les fiches restreintes que vous ne
voyez pas sont omises ; les notes protégées seulement là où vous pouvez les lire) et, pour les administrateurs,
**Export complet** avec toutes les fiches restreintes et notes protégées. Un export complet est inscrit au journal des
accès, avec un motif facultatif.

### La page Accès

Les administrateurs ouvrent **Accès** dans le menu du compte : la règle et ses deux exceptions, les rôles et le
nombre de personnes de chacun, les fiches partagées avec chaque invité, ce que chaque équipe possède et a restreint,
et le **journal des accès** (chaque ouverture d'une fiche restreinte ou de notes protégées par quelqu'un hors de
l'équipe propriétaire, chaque lecture par un administrateur, chaque export complet). Filtrez-le par personne, fiche,
type, nature et dates ; les lignes sont gardées 365 jours.

## Agents IA

Un agent IA (Claude, Claude Code, Copilot, Cursor, tout client MCP) travaille avec votre compte : il lit ce que vous
voyez, dans la limite du plafond fixé par votre administrateur, et écrit des brouillons et des propositions. Il ne
publie jamais seul.

1. Ouvrez **Mes agents** dans le menu du compte et copiez l'**adresse du serveur MCP**. Les guides en dessous donnent
   la configuration pour un client MCP, la ligne de commande et VS Code.
2. L'agent ouvre une page Noosphere, **Demande de connexion**, qui liste ce qu'il pourra et ne pourra pas faire.
   Décochez « L'autoriser à écrire des brouillons » pour ne lui donner que la lecture, puis choisissez **Autoriser** ou
   **Refuser**.
3. Si votre administrateur exige un accord, la page le dit et l'agent attend ; **Annuler la demande** la retire.
   Sinon l'agent apparaît dans **Mes agents**.

**Mes agents** liste chaque connexion avec son état (actif, en pause à son quota horaire, en attente d'accord), ce
qu'elle peut faire, et **Révoquer**, qui coupe son accès tout de suite. En dessous : les brouillons et propositions
écrits par vos agents (les soumettre, les ouvrir ou les abandonner) et chacun de leurs appels, gardés 90 jours. Les
invités ne connectent pas d'agent.

Dans l'espace de validation, une proposition écrite par un agent porte la puce **Agent IA pour** la personne, avec le
nom de l'agent, et sa page explique qui l'a écrite. Les validateurs décident comme pour toute proposition ; le motif
d'un refus va à l'agent et à la personne.

Les administrateurs ouvrent **Agents IA** dans le menu du compte : l'**arrêt d'urgence** (révoque toutes les connexions
d'un coup), chaque connexion avec sa personne (accepter ou refuser celles en attente, révoquer), ce que l'agent
sélectionné peut faire, toute l'activité avec des filtres par type et par agent, et les réglages : serveur MCP actif
ou non, plafond, quota horaire, qui peut connecter, enregistrement dynamique et llms.txt sans connexion. Voir
[Agents IA](agents.md) pour le détail.

## Liaisons et déploiements

Noosphere peut être lié à d'autres applications, comme un outil de mise en production ou une chaîne de livraison.
Leurs déploiements s'affichent alors sur les fiches d'application et de flux, dans un bloc **Déploiements et
changements** après la description : ceux à venir d'abord, puis les trois derniers, chacun nommant l'application qui
l'a envoyé. Choisissez une entrée pour l'ouvrir dans cette application ; les entrées sont en lecture seule ici. Les
invités ne voient pas ce bloc.

Les administrateurs gèrent les liaisons sur la page **Liaisons** du menu du compte : appairer une application avec un
code à usage unique, comparer l'empreinte de quatre mots affichée sur les deux écrans avant de confirmer, choisir ce
que chaque application peut faire, lire le journal des échanges, renouveler les clés ou couper une liaison. Voir
[Liaisons](liaisons.md).

## Boîte à outils de démo

Sur une instance de démo (`NOOSPHERE_DEMO=true`), l'en-tête porte un repère **Démo** et un bouton **Démo** reste dans
le coin bas (un panneau qui monte du bas sur téléphone). Il ouvre la boîte à outils :

- **Être quelqu'un d'autre** : changer de personnage sans mot de passe. Vous restez sur la page si le nouveau
  personnage peut la voir (décochez « Rester sur cette page quand je change » pour revenir toujours au paysage).
- **Simuler des collègues** : un collègue modifie la fiche ouverte (votre prochain enregistrement reçoit le conflit, et
  les propositions ouvertes dessus deviennent dépassées), un collègue vous envoie une proposition, un validateur
  accepte ou refuse votre dernière proposition, l'invitée lit une fiche partagée.
- **Agent IA** : un agent de démo, qui agit pour vous, écrit un brouillon, soumet une proposition, est refusé au-dessus
  du plafond ou atteint son quota.
- **Réinitialiser la démo**, après confirmation.

Chaque action affiche un avis avec un lien **Voir** vers ce qui a changé. Voir [Boîte à outils de démo](demo.md).

## Équipes

Les administrateurs gèrent l'appartenance aux équipes sur la page **Utilisateurs** (les **Équipes** de chaque compte)
et sur la fiche d'une équipe (**Gérer les membres**). La fiche d'une équipe liste les applications et les flux qu'elle
possède.

## Corbeille

Les fiches supprimées gardent leur historique. Les administrateurs les retrouvent dans la **Corbeille** (menu du
compte) et peuvent les restaurer.

## Réglages de l'instance

Les administrateurs ouvrent **Réglages** dans le menu du compte pour définir :

- le **nom de l'instance**, affiché dans l'en-tête et l'onglet du navigateur (par défaut « Noosphere ») ;
- le **nom de l'organisation**, affiché dessous (vide, l'en-tête affiche « Carte des flux ») ;
- la **langue par défaut** des nouveaux comptes et de la page de connexion.

Le nom technique `noosphere` (conteneur, CLI, format d'échange) ne change pas.
