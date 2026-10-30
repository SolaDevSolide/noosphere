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

Un propriétaire peut restreindre une application ou une page et lister les équipes et les personnes qui la voient ;
les autres voient seulement qu'elle existe. Nommer un invité sur une fiche la partage avec lui. Les notes protégées
gardent les détails d'exploitation (qui appeler, où est le compte de secours) pour les propriétaires et la liste
d'accès. **Qui peut voir et modifier ?** sur une fiche explique qui la lit, la publie et la propose. Coller un jeton,
une clé ou un mot de passe est refusé : écrivez plutôt où se trouve le secret. Détails :
[accès, notes protégées et journal des accès](acces.md).

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
