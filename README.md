# Radio IN

L’application officielle pour écouter **Radio IN en direct**, consulter les derniers titres diffusés et demander vos morceaux depuis un lecteur compact.

**[Écouter la radio dans le navigateur](https://radio-in.duckdns.org/public/radio_in)** · **[Téléchargements et notes de version](https://github.com/Inmouved/Radio-IN/releases)** · **[Signaler un problème](https://github.com/Inmouved/Radio-IN/issues/new)**

## Télécharger

| Plateforme | Version et disponibilité |
| --- | --- |
| **Windows** | **Bêta 1.4** disponible — installateur ou version portable |
| **Android** | **Bêta 1.3 Android** disponible — APK universel (Android 8+) |

### Android — Bêta 1.3

- **[Télécharger l’APK Android](https://github.com/Inmouved/Radio-IN/releases/download/android-v1.3.0-beta/RadioIN-Beta-1.3-Android.apk)**
- **[Vérifier le fichier téléchargé (SHA-256)](https://github.com/Inmouved/Radio-IN/releases/download/android-v1.3.0-beta/RadioIN-Beta-1.3-Android.apk.sha256)**
- **[Lire les nouveautés et les instructions d’installation](https://github.com/Inmouved/Radio-IN/releases/tag/android-v1.3.0-beta)**
- **[Consulter le code source Android](android/README.md)**

Android Auto avec direct, file des titres à venir, favoris, demandes de titres, ajout aux favoris, commandes vocales et messages de reconnexion. L’application propose aussi la reprise après perte réseau, l’économie de données pour les pochettes, la gestion des interruptions audio et un widget d’accueil. Installation hors Google Play, compatible Android 8.0 ou supérieur sur appareils 32 et 64 bits. Android Auto nécessite Android 9 ou supérieur.

### Windows — Bêta 1.4

- **[Télécharger l’installateur Windows (.exe)](https://github.com/Inmouved/Radio-IN/releases/download/windows-v1.4.0-beta/RadioIN-Beta-1.4-Setup.exe)**
- **[Télécharger la version portable (.zip)](https://github.com/Inmouved/Radio-IN/releases/download/windows-v1.4.0-beta/RadioIN-Beta-1.4-Windows.zip)**
- **[Lire les nouveautés de la Bêta 1.4](https://github.com/Inmouved/Radio-IN/releases/tag/windows-v1.4.0-beta)**
- **[Consulter le code source Windows](windows/README.md)**

**Configuration requise :** Windows 10 ou 11, 64 bits, avec .NET Framework 4.8 ou supérieur et une connexion Internet. Le moteur audio est inclus : VLC n’a pas besoin d’être installé.

> Les versions actuelles sont des bêtas. Les retours permettent d’améliorer le lecteur et de corriger les problèmes rencontrés.

## Fonctionnalités Windows

### Écouter et retrouver les titres

- Écoute du direct avec le titre, l’artiste et la pochette du morceau.
- Historique des derniers titres diffusés, historique de session et jusqu’à 1 000 écoutes conservées après fermeture, avec recherche par titre, artiste et date.
- File **À venir** : pochettes, artistes, demandes auditeur et horaires estimés, actualisés toutes les 15 secondes.
- Accès à **À venir** depuis le menu **⋯** et directement depuis les modes Compact et Large.
- Catalogue avec recherche et demandes de titres.
- **Mes demandes** avec suivi des demandes acceptées, heure estimée si disponible et alerte facultative lors de l’annonce dans la file.
- Favoris : ajout depuis le lecteur, y compris en mode Pochette, recherche, tri et copie du titre.
- Messages de confirmation, de refus et d’erreur de demande intégrés au lecteur.
- Export et import des favoris et des réglages.
- Sauvegardes automatiques des favoris : les 20 dernières copies sont conservées, avec restauration par fusion.
- Avertissement avant de redemander un titre déjà présent dans la file annoncée.

### Personnaliser le lecteur

Quatre modes d’affichage :

| Mode | Affichage |
| --- | --- |
| **Compact** | Lecteur et accès à l’historique et aux demandes |
| **Réduit** | Nom de la radio et titre diffusé ; commandes accessibles au survol |
| **Large** | Lecteur, historique et catalogue de demandes affichés ensemble |
| **Pochette** | Grande couverture carrée avec commandes au survol |

Six designs sont disponibles : **Actuel, Sombre, Flamme, Vert, Bleu et Auto**.

L’opacité est réglable de **70 à 100 %**, avec trois tailles de titres et de listes et des animations normales, discrètes ou désactivées. Chaque mode mémorise sa position et le mode Compact adapte ses commandes à la largeur disponible.

Le lecteur propose également des transitions douces, des titres défilants, une animation discrète pendant l’écoute, un logo de remplacement lorsqu’une pochette manque et une option pour garder la fenêtre au premier plan.

### Garder le contrôle de l’écoute

- Réglage du volume à la position choisie dans la barre et bouton **Son/Muet**, avec conservation du volume.
- Indicateur de connexion : arrêté, chargement, connexion stable, muet ou reconnexion.
- Minuterie de sommeil personnalisable de **1 à 1 440 minutes**.
- Baisse progressive du volume avant l’arrêt : désactivée, 15, 30 ou 60 secondes.
- Reconnexion automatique avec compte à rebours et bouton **Réessayer maintenant**.
- Touches multimédias Lecture/Pause et Arrêt, avec option d’utilisation en arrière-plan.
- Réduction près de l’horloge Windows.
- Notifications de changement de titre et options de lancement configurables.

Les demandes dépendent du catalogue et des règles de la radio. Une demande acceptée n’entraîne pas une lecture immédiate. Le délai observé est d’environ **10 à 15 minutes**, à titre indicatif. Les horaires de la file peuvent évoluer et toutes les demandes acceptées n’y figurent pas nécessairement déjà.

## Installer sur Windows

### Avec l’installateur

1. Fermez l’ancienne version de Radio IN si elle est ouverte.
2. Téléchargez **RadioIN-Beta-1.4-Setup.exe**.
3. Lancez le fichier et suivez l’assistant.
4. Ouvrez Radio IN depuis le menu Démarrer ou le raccourci choisi.

L’installation concerne votre compte Windows et ne demande pas de droits administrateur. Le raccourci Bureau est facultatif.

### En version portable

1. Téléchargez **RadioIN-Beta-1.4-Windows.zip**.
2. Extrayez **tout le contenu** dans un dossier accessible en écriture.
3. Lancez **RadioIN.exe** depuis ce dossier.

Conservez les fichiers DLL et le dossier `plugins` avec le programme. Ne lancez pas le lecteur directement depuis l’archive ZIP.

Pour télécharger l’application, choisissez les fichiers joints à la release. Les archives automatiques **Source code (zip)** et **Source code (tar.gz)** proposées par GitHub ne sont pas les versions portables du lecteur.

## Mettre à jour

Dans le lecteur : **menu ⋯ → Rechercher une mise à jour**.

La vérification au démarrage est activée par défaut et peut être désactivée. Le téléchargement laisse la radio jouer ; l’application vérifie le fichier avant de proposer son installation.

- **Version installée :** l’installateur EXE est privilégié.
- **Version portable à partir de la Bêta 1.2 :** les prochaines mises à jour peuvent utiliser le ZIP, avec copie de secours et restauration des fichiers remplacés si une erreur survient pendant l’opération.
- **Depuis la Bêta 1.1 :** le passage à la Bêta 1.3 utilise l’installateur EXE.
- **Ancienne version sans recherche intégrée :** téléchargez la nouvelle version depuis les releases et installez-la manuellement.

Vos favoris et réglages sont conservés lors des mises à jour.

## Raccourcis clavier

| Raccourci | Action |
| --- | --- |
| `Ctrl + E` | Lecture / arrêt |
| `Ctrl + ↑` / `Ctrl + ↓` | Ajuster le volume |
| `Ctrl + M` | Passer au mode d’affichage suivant |
| `Ctrl + D` | Passer au design suivant |
| `Ctrl + F` | Ajouter le titre en cours aux favoris |
| `Ctrl + L` | Ouvrir les favoris |
| `Ctrl + H` | Ouvrir l’historique |
| `Ctrl + R` | Ouvrir les demandes de titres |
| `Ctrl + N` | Activer ou désactiver les notifications de titre |

Le bouton **−** réduit le lecteur près de l’horloge. Le bouton **×** ferme l’application et arrête le son.

## Favoris, réglages et diagnostic

Les données Windows sont conservées localement dans :

```text
%LOCALAPPDATA%\RadioIN
```

Le menu **⋯** permet d’exporter ou d’importer une sauvegarde. La réinitialisation des réglages conserve les favoris.

Les journaux de diagnostic restent sur votre PC : aucun envoi automatique. **Menu ⋯ → À propos** permet d’ouvrir les dossiers utiles. La désinstallation Windows conserve les favoris et les réglages personnels.

## Nouveautés de la Bêta 1.3

La file **À venir**, les sauvegardes automatiques des favoris, le bouton Muet, les réglages de lisibilité et l’historique de session sont disponibles.

**[Consulter les notes complètes de la Bêta 1.3](https://github.com/Inmouved/Radio-IN/releases/tag/windows-v1.3.0-beta)**

La Bêta 1 Android est publiée pour essai hors Google Play.

## Signaler un problème ou proposer une idée

Ouvrez une **[issue GitHub](https://github.com/Inmouved/Radio-IN/issues/new)** en précisant :

- La version de Radio IN et votre version de Windows.
- Les étapes pour reproduire le problème.
- Le résultat attendu et ce qui se produit réellement.
- Une capture d’écran si elle aide à comprendre le problème.

Si vous joignez un journal, retirez les informations personnelles que vous ne souhaitez pas publier. Ne publiez jamais de mot de passe ni de clé API.

---

**Radio IN** · **[Écouter en ligne](https://radio-in.duckdns.org/public/radio_in)** · **[Toutes les versions](https://github.com/Inmouved/Radio-IN/releases)**
