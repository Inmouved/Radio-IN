# Code source Windows

Ce dossier contient **Radio IN Windows 1.0**. Les quatre modes d’affichage et les six designs sont disponibles. Chaque mode mémorise sa position. Le menu de l’icône près de l’horloge permet de recentrer le lecteur.

Les paramètres regroupent l’apparence, l’écoute, les notifications, les mises à jour et les sauvegardes. L’historique personnel conserve jusqu’à 1 000 écoutes et propose un filtre pour la session en cours. Les dernières diffusions et le suivi des demandes sont consultables séparément.

Radio IN affiche **1.0**. Le tag GitHub `windows-v2.0.0` et la version de fichier `2.0.0.0` permettent aux bêtas Windows de détecter et de vérifier la mise à jour.

## Prérequis de compilation

- Windows 10 ou 11, 64 bits.
- .NET Framework 4.8 et le SDK .NET 8.
- VLC installé dans C:\Program Files\VideoLAN\VLC pour récupérer le moteur audio et les modules indispensables.
- Inno Setup 6.7.3 ou supérieur pour construire l’installateur.

## Construire

Depuis ce dossier, lancez build-lite.ps1 pour créer la version portable, build-installer.ps1 pour l’installateur, ou build-release.ps1 pour la distribution complète et ses empreintes SHA-256.

Les fichiers de cette version sont générés dans `outputs/windows/1.0` à la racine du dépôt et sont ignorés par Git. Les clés de signature, réglages personnels, journaux et données de test ne font pas partie du dépôt.

## Structure

- MainWindow.xaml et MainWindow.xaml.cs : interface et lecture.
- Features.cs, BetaFeatures.cs, Release13.cs, Upcoming.cs : fonctions du lecteur.
- Updater.cs et PortableUpdate.cs : recherche et installation des mises à jour.
- installer/ : script Inno Setup de l’installateur Windows.
- assets/ : icône et logo de l’application.

Les fichiers de tests font partie du code source et ne soumettent aucune demande musicale réelle.
