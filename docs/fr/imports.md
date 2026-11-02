# Imports depuis d'autres formats

[English](../en/imports.md)

Noosphere lit la documentation que les équipes ont déjà et la transforme en enregistrements du catalogue. Tout import
commence par une **simulation** : elle montre ce qui serait créé, mis à jour ou laissé tel quel, et rien n'est écrit
tant que ce même plan n'est pas appliqué. Un import ne **supprime jamais** rien.

| Format | Fichiers | Devient | Page |
| ------ | -------- | ------- | ---- |
| Backstage | `catalog-info.yaml` (un ou plusieurs documents) | domaines, équipes, applications, flux | [Backstage](imports-backstage.md) |
| OpenAPI 3.x | `.yaml`, `.json` | une page sur l'application, des flux depuis ses consommateurs | [OpenAPI](imports-openapi.md) |
| AsyncAPI 2.x et 3.x | `.yaml`, `.json` | des flux événementiels via le bus, des objets de données | [AsyncAPI](imports-asyncapi.md) |
| ADR (MADR, Nygard) | `.md` | des pages de décisions | [ADR](imports-adr.md) |
| Mermaid (flowchart, C4) | `.mmd`, `.mermaid` | des applications et des flux | [Mermaid](imports-mermaid.md) |
| Excel | `.xlsx`, à partir du [modèle](imports-excel.md#modèle) | domaines, équipes, applications, objets de données, flux | [Excel](imports-excel.md) |

On peut envoyer plusieurs fichiers ensemble, et des archives zip de ces fichiers (par exemple un dossier d'ADR). Le
format de chaque fichier est reconnu d'après son extension et son contenu ; l'option `format` l'impose. Un export
Noosphere s'importe avec l'[import du format d'échange](exchange-format.md).

## Simulation, aperçu, application

Dans l'interface, la page **Import et export** suit les mêmes étapes : source, essai à blanc avec l'aperçu, lignes à
écarter, confirmation et résultat. Voir le [guide utilisateur](guide-utilisateur.md#imports).

1. **Simulation.** `POST /api/imports` avec les fichiers (`multipart/form-data`, champ `files`). Noosphere les lit,
   retrouve les enregistrements existants, vérifie chaque changement avec les mêmes règles que les formulaires et
   enregistre le résultat comme **plan**. Il répond `201` avec le plan.
2. **Aperçu.** Le plan liste ses entrées, chacune avec son action (`create`, `update` ou `unchanged`), le diff champ
   par champ (tous les champs remplis pour une création, les champs modifiés pour une mise à jour), la façon dont
   l'enregistrement existant a été trouvé (`matchedBy`), les **correspondances possibles** (enregistrements existants
   au nom proche) et l'origine de l'entrée : fichier (avec son chemin dans l'archive), ligne et emplacement
   (`documents[3]`, `channels.billing.invoice.issued.subscribe`, `Applications!A4`). Les avertissements disent ce qui
   a été laissé de côté ou déduit ; les erreurs, ce qui empêche l'import.
3. **Application.** `POST /api/imports/{id}/apply` écrit exactement le plan qui a été montré, ou une partie : le corps
   `{ "skip": [3, 7] }` écarte les entrées de ces index (créations et mises à jour). Une entrée liste dans `requires`
   les entrées qui créent les enregistrements auxquels elle renvoie (son domaine, son équipe, les extrémités d'un
   flux...) : une entrée gardée ne peut pas dépendre d'une entrée écartée. Le plan appliqué liste les entrées écartées
   dans `skipped`.

Un plan s'applique une seule fois, par la personne qui l'a fait, dans les **30 minutes**, et seulement si le catalogue
n'a pas changé depuis la simulation :

| Situation | Réponse |
| --------- | ------- |
| Appliqué | `200`, le plan avec `status: applied` |
| Plan de quelqu'un d'autre, ou inconnu | `404` |
| Déjà appliqué ou refusé | `409` |
| Un enregistrement a été créé, modifié, supprimé ou restauré depuis la simulation (par qui que ce soit) | `409`, et le plan passe à `rejected` : relancez la simulation |
| Plus de 30 minutes après la simulation | `410`, le plan passe à `expired` |
| Le plan a des erreurs | `422` |
| Une entrée gardée dépend d'une entrée écartée, ou tous les changements sont écartés | `422`, le plan reste `planned` |

## Correspondance

Chaque entrée d'un fichier est rapprochée d'un enregistrement existant du même type :

1. par son **identifiant stable** : l'id Noosphere donné par la source (`metadata.name` Backstage ou l'annotation
   `noosphere.io/id`, l'alias C4, la colonne `id` Excel, le nom du fichier d'ADR...) ;
2. sinon par son **nom exact**, une fois normalisé : casse, accents et ponctuation sont ignorés, donc `Réseau Auto+` et
   `reseau auto` sont le même nom. Un flux correspond à un flux de même nom entre les deux mêmes applications.

Les enregistrements existants dont le nom est seulement **proche** (l'un est l'autre plus un mot, ou quelques lettres
diffèrent) sont montrés comme correspondances possibles, et jamais fusionnés : l'entrée crée un nouvel
enregistrement. Quand plusieurs enregistrements existants ont le même nom, ou que deux entrées visent le même
enregistrement, l'entrée est ignorée avec un avertissement : donnez son id dans le fichier.

Les références entre enregistrements (une équipe, un domaine, les extrémités d'un flux, les objets de données qu'il
transporte) s'écrivent par id ou par nom : Noosphere cherche d'abord dans les fichiers, puis dans le catalogue. Une
référence qui ne correspond à rien est laissée de côté avec un avertissement ; un flux dont une extrémité est
inconnue est ignoré.

## Ce que change un import

- Il **crée** des enregistrements et **met à jour** des champs ; il ne supprime jamais d'enregistrement et ne vide
  jamais de champ. Un champ que le fichier ne donne pas garde sa valeur. Les étiquettes et les objets de données d'un
  flux s'ajoutent à ceux qui existent.
- Les enregistrements qu'un fichier ne fait que **mentionner** (l'application d'un fichier OpenAPI, le bus d'un
  fichier AsyncAPI, les nœuds d'un diagramme Mermaid, les messages d'un fichier AsyncAPI) sont créés s'ils n'existent
  pas ; s'ils existent, seuls leurs champs vides sont remplis.
- Une nouvelle application sans domaine va dans le domaine donné par l'option `domainId`, sinon dans un domaine
  **Imported** (créé au besoin, avec un avertissement). L'option `teamId` est l'équipe propriétaire des nouvelles
  applications, des nouveaux flux et des nouvelles pages qui n'en ont pas.
- La visibilité, les listes d'accès et les notes protégées ne sont jamais posées par un import : les enregistrements
  restreints sont créés par les administrateurs, à la main. Un texte qui ressemble à un secret (jeton, clé, mot de
  passe) est une erreur, comme dans les formulaires.

## Droits

Seuls les administrateurs importent, comme pour l'[import d'échange](exchange-format.md) : membres et invités
reçoivent `403` sur la simulation, les plans et leur application. Les membres envoient plutôt une
[proposition](proposals.md). Le [modèle Excel](imports-excel.md#modèle) est ouvert à toute personne connectée. Les
plans de la ligne de commande appartiennent à `system`, lancée par qui exploite le serveur.

## Options

| Option | Sens |
| ------ | ---- |
| `format` | `backstage`, `openapi`, `asyncapi`, `adr`, `mermaid` ou `excel` : lire tous les fichiers dans ce format. |
| `domainId` | Domaine des nouvelles applications qui n'en ont pas (un domaine existant). |
| `teamId` | Équipe propriétaire des nouveaux enregistrements qui n'en ont pas (une équipe existante). |
| `applicationId` | Application que décrit un fichier OpenAPI ou AsyncAPI, ou à laquelle les décisions sont rattachées (id ou nom). |
| `parentPageId` | Page sous laquelle vont les décisions (une page existante). |

## Limites

| Limite | Valeur |
| ------ | ------ |
| Un envoi (tous les fichiers) | 20 Mo |
| Un fichier, une fois extrait de son archive | 5 Mo |
| Fichiers, archives ouvertes | 200 |
| Tout ce qui est extrait des archives d'un import | 50 Mo |
| Taux de compression d'une entrée d'archive (bombes zip) | 100 |
| Enregistrements d'un plan | 5 000 |
| Lignes d'une feuille | 10 000 |
| Nœuds et profondeur d'un document YAML ou JSON | 200 000 et 64 niveaux |
| Documents YAML d'un fichier | 1 000 |

Une archive dans une archive n'est pas ouverte. Le YAML est lu de façon sûre : les alias (`*nom`) sont refusés, pour
qu'un petit fichier ne puisse pas se déployer en un fichier énorme (l'import du format d'échange les refuse aussi).

## Trace des imports

Chaque simulation est gardée, appliquée ou non : `GET /api/imports` liste tous les imports, avec leur auteur, leur date, leurs formats, leurs comptes, leur état et la raison d'un
refus ; `GET /api/imports/{id}` donne l'aperçu complet, avec le nom, la taille et le SHA-256 de chaque fichier.
Chaque enregistrement écrit reçoit une révision de la personne qui a appliqué le plan, avec le résumé
`Imported (backstage, import 1a2b3c4d)`, dans son [historique](history.md). Le journal du serveur a une ligne pour
chaque plan, chaque application et chaque refus.

## API

| Requête | Fait |
| ------- | ---- |
| `POST /api/imports?format=&domainId=&teamId=&applicationId=&parentPageId=` | Simulation : lit les fichiers, enregistre et renvoie le plan (`201`). |
| `GET /api/imports/{id}` | Le plan (pour son auteur seulement). |
| `POST /api/imports/{id}/apply` | Applique le plan ; `{ "skip": [3, 7] }` écarte des entrées. |
| `GET /api/imports?limit=50` | Les imports récents. |
| `GET /api/imports/template.xlsx` | Le [modèle Excel](imports-excel.md#modèle). |

```sh
curl -b cookies -H "X-Requested-With: fetch" -F files=@catalog-info.yaml -F files=@adr.zip \
  "https://noosphere.exemple.fr/api/imports?teamId=team-crm"
curl -b cookies -H "X-Requested-With: fetch" -X POST https://noosphere.exemple.fr/api/imports/<id>/apply
```

Une entrée du plan :

```json
{
  "index": 5,
  "action": "update",
  "kind": "application",
  "id": "crm",
  "name": "CRM",
  "matchedBy": "identifier",
  "changes": [
    { "field": "description", "label": "Description", "kind": "markdown",
      "before": "Customer master data and the history of every interaction.",
      "after": "Customer relationship management of Mutuelle Alizé." }
  ],
  "possibleMatches": [],
  "source": { "file": "catalog-info.yaml", "line": 49, "place": "documents[4]" },
  "hasErrors": false,
  "requires": []
}
```

## Ligne de commande

```sh
docker compose exec noosphere noosphere imports plan /data/catalog-info.yaml /data/adr.zip --team team-crm
docker compose exec noosphere noosphere imports apply <id-du-plan> --skip 3,7
docker compose exec noosphere noosphere imports template --output /data/modele.xlsx
```

`imports plan` affiche le plan (`--json` pour l'avoir en entier) et sort avec le code 1 s'il a des erreurs. Les plans
de la ligne de commande appartiennent à `system`, qui a tous les droits. Voir la [ligne de commande](cli.md).
