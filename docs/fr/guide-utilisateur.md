# Guide utilisateur : propositions, historique et réglages

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

## Historique

Chaque modification publiée crée une version : un enregistrement direct, une proposition acceptée ou une restauration.
Le bouton discret **Historique** d'une fiche ouvre un panneau latéral avec ses versions, leur auteur et leur date, et
un lien vers la proposition à l'origine d'une version.

- **Comparer** : cochez deux versions puis choisissez **Comparer** pour voir les champs qui ont changé entre elles.
- **Restaurer** : choisissez **Restaurer** sur une version antérieure et confirmez. Les propriétaires la republient
  dans une nouvelle version ; les autres envoient une proposition aux propriétaires.

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
