# Calcul du pointage dans Factorial

Cette extension Factorial Code ajoute un onglet privé à la page de son application dans Factorial HR. L’onglet est réservé aux administrateurs. La première version accepte l’**ID Factorial** d’un collaborateur et une période inclusive de 31 jours au maximum. L’ID est disponible dans l’annuaire du journal de pointage existant. Le nom affiché est lu dans l’annuaire Factorial au moment de la consultation.

## Contenu du rapport

- Une ligne par jour demandé et le détail des arrivées, des départs et des pauses classés par date de référence Factorial. Un départ après minuit est affiché avec sa date.
- **Travail calculé** additionne les intervalles de travail terminés. Les intervalles de même type qui se chevauchent sont fusionnés. Les plages de pause sont affichées et calculées séparément. Elles ne sont pas déduites une seconde fois des plages de travail distinctes. Les règles de pauses rémunérées et d’heures supplémentaires relèvent des calculs Factorial.
- **Temps suivi Factorial** affiche le champ quotidien `tracked_minutes` quand l’unité renvoyée est `minute`. Le détail conserve séparément le champ `minutes` des plages closes. Une donnée absente affiche `—`, un zéro renvoyé ou calculé affiche `0 min` et une plage ouverte rend le calcul de sa catégorie incomplet.
- Les heures sans décalage sont interprétées en `Africa/Casablanca`. Une heure répétée ou inexistante reste visible si l’API la fournit sous forme d’heure locale. La durée concernée est alors signalée comme incomplète.

Le processus utilise uniquement la lecture par ID de `employees/employees`, la liste `attendance/shifts` et la liste `attendance/worked_times`. Il utilise le client OAuth fourni par l’espace Factorial Code, suit les curseurs de l’API et vérifie l’entreprise du collaborateur et des plages. La version du client en service est fournie par `base-app`. Les tests locaux utilisent le SDK officiel `2.0.0` (API `2026-07-01`) avec des réponses HTTP simulées. Les entrées du formulaire et le résultat sont déclarés transitoires dans Factorial Code. [Client Factorial](https://code.factorialhr.com/docs/building-apps/factorial-client/) · [Entrées de formulaire](https://code.factorialhr.com/docs/processes/input-params/) · [Résultats transitoires](https://code.factorialhr.com/docs/processes/source-code/)

## Aperçu dans le navigateur

Depuis le dossier `factorial-code`, lancez `npm run preview` avec Node.js 22 ou plus récent, puis ouvrez [l’aperçu local](http://127.0.0.1:5082/). L’aperçu exécute le calcul de l’extension sur des données fictives. Le formulaire est prérempli avec l’ID `42` et la période du 16 au 20 novembre 2026. L’aperçu fonctionne sans identifiant ni connexion à Factorial.

## Tests locaux

Depuis le dossier `factorial-code`, avec Node.js 22 ou plus récent.

```powershell
npm ci
npm test
```

Les tests exécutent le code du processus avec le SDK officiel et simulent le contexte d’un formulaire Factorial. Ils vérifient notamment les requêtes GET, la pagination, le contrôle de l’entreprise, les dates et les changements d’heure au Maroc. Aucun compte ni jeton Factorial réel n’est utilisé.

## Installation en démonstration

1. Ouvrir le [portail Factorial Code](https://code.factorialhr.com/) avec un accès développeur approuvé. Créer une application **standard**, en **JavaScript**, et sélectionner les scopes OAuth `employees` et `time_tracking`. [Parcours développeur](https://code.factorialhr.com/docs/building-apps/developer-journey/) · [Scopes OAuth](https://apidoc.factorialhr.com/docs/oauth-scopes)
2. Cloner l’espace `dev-{app-id}` avec `fcode clone`, puis copier uniquement le dossier `processes/calcul-du-pointage` dans le dossier `processes` du clone. Depuis le clone, enregistrer le processus avec `fcode processes:add`, vérifier le résultat de `fcode diff`, puis envoyer le code dans l’espace de développement avec `fcode push`. [CLI Factorial Code](https://code.factorialhr.com/docs/cli/)
3. Installer la version de développement dans une entreprise de démonstration. Avec un compte administrateur, ouvrir la page de l’application, sélectionner **Calcul du pointage**, saisir un ID de collaborateur de démonstration et choisir une période contenant des pointages existants. Comparer les entrées et sorties avec **Suivi du temps** et vérifier l’affichage des deux tableaux. Refaire l’essai avec une période vide et avec un compte sans droit d’administration. Vérifier que le rapport apparaît à l’écran et que l’historique de l’exécution affiche la valeur transitoire. [Onglets Factorial](https://code.factorialhr.com/docs/factorial-actions/#forms-offered-on-the-app-page)
4. Demander une version via le portail après la validation en démonstration. L’installation chez Le Chameau SCZ Morocco passe ensuite par l’administration Factorial et son processus de publication. [Processus de publication](https://code.factorialhr.com/docs/building-apps/developer-journey/)

L’extension actuelle consulte les pointages enregistrés dans **Suivi du temps**. Les saisies qui existent dans le journal SQL Server sont accessibles depuis ce journal. La documentation publique consultée décrit les onglets sur la page de l’application et ne confirme aucun emplacement d’extension dans le panneau natif **Détails du planning**.
