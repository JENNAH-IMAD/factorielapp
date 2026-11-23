# Contrat Factorial vérifié le 19 novembre 2026

Source machine téléchargée : https://api.factorialhr.com/oas/?version=2026-10-01 ; copie dans `factorial-openapi.json`. Version stable `2026-10-01` confirmée par https://apidoc.factorialhr.com/docs/api-versioning (2027-01-01 est une release candidate).

Sources officielles :
- https://apidoc.factorialhr.com/docs/getting-started
- https://apidoc.factorialhr.com/docs/time-tracking-integrations
- https://apidoc.factorialhr.com/llms.txt
- https://apidoc.factorialhr.com/docs/pagination
- https://apidoc.factorialhr.com/docs/oauth-scopes
- https://apidoc.factorialhr.com/docs/faqs

## Opérations utilisées (préfixe `/api/2026-10-01/resources/`)

| Méthode / chemin | Champs et décisions |
|---|---|
| GET `api_public/credentials` | Enveloppe `{data,meta}` ; `company_id` confirme l’identité. |
| GET `employees/employees` | `id`, `company_id`, `full_name`, `company_identifier`. Scopes OAuth : `employees`. `only_active=false`, `only_managers=false` incluent les employés non managers et non actifs. |
| GET `attendance/shifts` | `employee_ids[]`, `start_on`, `end_on`, `half_day=false`, `sort_created_at_asc=false`, `breaks_with_time_configuration=true`. Ne pas filtrer `workable` pour inclure les pauses. Scope `time_tracking`. |
| POST `attendance/shifts` | Plage terminée : `employee_id`, `date`, `clock_in`, `clock_out`, `workable=true`, `source=api`. |
| POST `attendance/shifts/clock_in` | `employee_id`, `now` ISO 8601 avec décalage. |
| POST `attendance/shifts/clock_out` | `employee_id`, `now`. |
| POST `attendance/shifts/break_start` | `employee_id`, `now`, `time_settings_break_configuration_id` si nécessaire. |
| POST `attendance/shifts/break_end` | `employee_id`, `now`. |

L’écriture retourne directement un objet Shift, **pas** l’enveloppe paginée. Les IDs sont des chaînes dans cette version du schéma. Les paramètres `limit` et `after_id` sont communs aux listes dans le guide Pagination, même s’ils ne sont pas répétés dans chaque opération OpenAPI.

Le schéma Shift fournit `date`, `reference_date`, `clock_in`, `clock_out`, `clock_in_with_seconds`, `workable`, `minutes`, `created_at`, `updated_at`, `company_id`, `employee_id`, `id`. `reference_date` représente le jour d’imputation, qui peut différer du jour civil d’une plage de nuit. Les exemples de lecture utilisent des heures sans offset ; le guide demande des horodatages ISO avec offset à l’écriture. Le connecteur conserve cette différence et ne prétend pas disposer d’un instant certain quand le fuseau d’origine n’est pas fourni.

## Capacités laissées désactivées

Le filtre Shift `updated_at` existe mais sa sémantique comparative n’est pas assez explicite pour établir un watermark sûr. `employees.updated_at_gteq` est documenté à la journée ; la lecture complète reste utilisée pour repérer aussi les absences.

Les événements `attendance/shift/*` sont mentionnés par le guide ; aucune URL webhook publique ni souscription n’est créée. Le guide des pauses comporte un lien répété : l’implémentation se fonde sur les chemins distincts `break_start` et `break_end` présents dans l’OpenAPI.

L’API ne garantit pas d’idempotence des écritures. Le journal local et le rapprochement évitent le renvoi automatique ; une réponse perdue peut exiger une intervention. Une égalité de minute ne prouve pas une égalité de seconde.

OAuth partenaire exige un jeton d’entreprise et les scopes `employees time_tracking`. Le transport Bearer est disponible pour un jeton provisionné côté serveur ; l’acquisition et le renouvellement automatisés ne sont pas implémentés. La clé API a des droits larges qui ne peuvent pas être réduits par le connecteur ; le mode lecture seule est une restriction interne de l’application.
