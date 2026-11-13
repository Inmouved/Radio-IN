# Radio IN — Bêta 1.2 Android

Application native Kotlin, Android 8.0/API 26 minimum, cible Android 16/API 36.
Un APK universel, sans bibliothèque native ni VLC, pour les architectures ARM et x86.

## Fonctions

Lecture HTTPS en service MediaLibraryService/ExoPlayer : arrière-plan, média système,
écran verrouillé, Android Auto (direct et favoris avec action de demande), gestion du
focus audio, interruptions et débranchement des écouteurs. Android Auto nécessite
Android 9/API 28 ou supérieur ; le lecteur reste compatible dès Android 8/API 26.
Écran compact avec design automatique fondé sur la pochette. Historique, file « À venir »
et demandes de titres sont accessibles depuis le lecteur. Les listes de titres peuvent
être ajoutées aux favoris. Historique de session local, recherche de mises à jour Android,
retours de demande non bloquants. Pochettes, catalogue AzuraCast, recherche, pagination,
confirmation des demandes et respect de Retry-After. Favoris locaux, export/import JSON
compatible Windows, minuterie, reconnexion, volume, lecture automatique à l'ouverture
facultative, notifications de titre facultatives, écran allumé facultatif, partage du titre
et diagnostic local. Reconnexion déclenchée par le retour réseau et reprise automatique
après interruption audio quand Android rend le focus. Option d’économie mobile qui utilise
les pochettes en cache et évite leur téléchargement sur le réseau cellulaire. Widget
d’accueil redimensionnable (4×1 ou 2×2) pour lancer/arrêter Radio IN et voir le titre.
Les fonctions propres à Windows sont adaptées : notification média au lieu de la zone
de notification, écran allumé au lieu de « toujours visible », retour au lecteur depuis
la notification au lieu d'une seconde fenêtre. Aucun lancement au démarrage du téléphone.

## Construire

JDK 17 ou 21, Gradle 8.13, SDK Android 36 et Build Tools 36.0.0.
Les versions des dépendances sont fixées dans les fichiers Gradle.
Ouvrir ce projet dans Android Studio ou utiliser le wrapper Gradle inclus :

```text
gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

Pour le même APK signé que cette livraison : exécuter `build-android.ps1` en indiquant
les chemins des outils et de la clé si nécessaire. Le script ne génère jamais une
nouvelle clé silencieusement. Les secrets passent par l'environnement du processus.

Clé initiale conservée uniquement dans le dossier privé local du projet :
`../../private/RadioIN-Android-signature/radioin-beta.jks` et `store-password.txt`.
Ces deux fichiers sont à sauvegarder ensemble dans un emplacement privé sécurisé.
Ils ne sont inclus ni dans l'APK ni dans l'archive des sources. Ne pas les publier.
La même clé et un `versionCode` supérieur sont nécessaires pour les mises à jour.
Application ID stable : `org.radioin.player` ; alias de signature : `radioin`.

## Vérification

Tests Robolectric sur API 26, 28, 33, 35 et 36 : sauvegardes Windows/Android,
validation sans perte des fichiers refusés, favoris, réglages, écran compact et ses
sections, création/libération du service, parsing des API et demandes simulées
avec refroidissement serveur. Aucune demande réelle n'est envoyée par ces tests.
Test de rendu graphique natif de l'écran compact sur un écran 393 × 851 dp.
Captures dans `app/build/previews/` ; il s'agit de données de démonstration.

L'analyse Lint échoue en présence d'erreurs de compatibilité. Les avertissements
de nouvelles versions de bibliothèques ne signifient pas un échec des contrôles.
Les versions utilisées ici sont épinglées, plutôt que résolues dynamiquement.

Les émulateurs complets n'ont pas pu démarrer sur ce PC ; les tests JVM Android et
les captures natives ne valident pas la sortie audio, le Bluetooth ou les politiques
d'économie d'énergie d'un constructeur. Voir la checklist fournie pour les essais
sur téléphone réel, notamment sur Xiaomi 14/HyperOS.

## Données et réseau

API et audio : `https://radio-in.duckdns.org`. Pochettes : URL HTTPS indiquée par la radio.
Actualisation toutes les 15 secondes, recherche après 450 ms d'inactivité.
Les demandes POST sont envoyées uniquement après confirmation explicite.
Les sauvegardes sont choisies via le sélecteur de documents Android ; aucune
permission générale sur le stockage. Pas de compte, publicité ou télémétrie ajoutés.
Les journaux tournent à 512 Ko et restent internes ; le partage passe par un choix utilisateur.
L'export contient favoris, design, mode et volume pour rester compatible avec Windows.
Cette version force le design automatique et l'écran compact ; elle importe les favoris
et le volume, mais conserve ces deux choix d'affichage. Elle n'active pas la lecture
automatique, les notifications ou d'autres permissions.

Code du lecteur Media3 : https://github.com/androidx/media (Apache 2.0).
Documentation de l'architecture audio : https://developer.android.com/media/media3/session/background-playback
