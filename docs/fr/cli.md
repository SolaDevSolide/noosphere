# Ligne de commande

[English](../en/cli.md)

La commande `noosphere` est le même exécutable que le serveur et lit les mêmes variables `NOOSPHERE_*`. Elle
travaille directement sur la base : aucune session n'est nécessaire. Les commandes, options et messages sont en
anglais. Dans le conteneur :

```sh
docker compose exec noosphere noosphere help
```

Hors Docker : `dotnet Noosphere.dll <commande>`.

| Commande | Effet |
| -------- | ----- |
| `noosphere migrate` | Applique les migrations de la base en attente (avec la sauvegarde décrite dans l'[installation](installation.md#mises-à-jour)). |
| `noosphere users list` | Liste les comptes. |
| `noosphere users create <username> [--role admin\|member\|guest] [--display-name <nom>] [--language en\|fr] [--password <p>]` | Crée un compte (`member` par défaut). Sans `--password`, un mot de passe aléatoire est généré et affiché une seule fois. |
| `noosphere users reset-password <username> [--password <p>]` | Pose un nouveau mot de passe (généré s'il n'est pas donné) et ferme les sessions du compte. |
| `noosphere export [--format json\|yaml] [--output <fichier>]` | Écrit tout le paysage et ses pages, enregistrements restreints et notes protégées compris (`scope: full`), sur la sortie standard ou dans un fichier. |
| `noosphere import <fichier\|-> [--dry-run] [--format json\|yaml]` | Vérifie un fichier, puis crée ou met à jour ses éléments. `-` lit l'entrée standard. Le format vient de l'extension, ou est deviné. |
| `noosphere demo reset --yes` | Remplace tout le paysage, ses pages, son historique et ses propositions par la démonstration « Alizé » et réinitialise les comptes de démonstration. Mode démonstration seulement. |
| `noosphere link code [--expires 15m] [--capabilities <liste>]` | Crée un code d'appairage pour gestion-mep, affiché une fois. Voir [liaisons](liaisons.md#ligne-de-commande). |
| `noosphere link list` | Liste les liaisons avec des instances de gestion-mep. |
| `noosphere link confirm <id> --fingerprint "<quatre mots>"` | Confirme une liaison après comparaison des empreintes. |
| `noosphere link renew <id>` | Renouvelle les clés de ce côté d'une liaison. |
| `noosphere link cut <id> --yes` | Coupe une liaison et efface ses clés. |
| `noosphere health [--url <url>]` | Renvoie 0 quand le serveur en marche est prêt. Sert au `HEALTHCHECK` de l'image. |
| `noosphere version` | Affiche la version. |

## Sorties

Les résultats vont sur la sortie standard, les messages et les erreurs sur la sortie d'erreur. `--json` rend les deux
lisibles par un programme : les commandes de données (`users list`, `link list`) écrivent leurs données, les autres
`{code, message, result}`. Les erreurs sont écrites sous la forme `{code, error, details}`.

## Codes de retour

| Code | Sens |
| ---- | ---- |
| 0 | Succès |
| 1 | Échec : fichier invalide, valeur refusée, mode démonstration inactif |
| 2 | Commande mal formée : commande ou option inconnue, argument manquant |
| 3 | Base injoignable ou configuration absente |
| 4 | Confirmation manquante (`--yes`) |
| 5 | Introuvable : compte, fichier ou liaison |

## Exemples

```sh
noosphere users create dana --role admin --display-name "Dana Martin"
noosphere export --format yaml --output paysage.yaml
noosphere import paysage.yaml --dry-run
docker compose exec -T noosphere noosphere import - < paysage.json
```
