# Code source Windows

Ce dossier contient le code source de la **Bêta 1.4 Windows** de Radio IN.

Le menu ⋯ donne accès à **Mes demandes**, à l’alerte facultative sur les demandes annoncées et à l’historique d’écoute. Le suivi rapproche les demandes acceptées de la file publique et des diffusions observées. L’historique conserve jusqu’à 1 000 passages écoutés et le suivi jusqu’à 200 demandes.

Le mode Pochette affiche le cœur des favoris au survol. Le mode Large donne un accès direct à **À venir**. Chaque mode mémorise sa position. Les mises à jour affichent la progression et permettent de relancer la recherche ou le téléchargement.

## Prérequis de compilation

- Windows 10 ou 11, 64 bits.
- .NET Framework 4.8 et le SDK .NET 8.
- VLC installé dans C:\Program Files\VideoLAN\VLC pour récupérer le moteur audio et les modules indispensables.
- Inno Setup 6.7.3 ou supérieur pour construire l’installateur.

## Construire

Depuis ce dossier, lancez build-lite.ps1 pour créer la version portable, build-installer.ps1 pour l’installateur, ou build-release.ps1 pour la distribution complète et ses empreintes SHA-256.

Les fichiers générés ne sont pas suivis par Git. Les clés de signature, réglages personnels, journaux et données de test ne font pas partie du dépôt.

## Structure

- MainWindow.xaml et MainWindow.xaml.cs : interface et lecture.
- Features.cs, BetaFeatures.cs, Release13.cs, Upcoming.cs : fonctions du lecteur.
- Updater.cs et PortableUpdate.cs : recherche et installation des mises à jour.
- installer/ : script Inno Setup de l’installateur Windows.
- assets/ : icône et logo de l’application.

Les fichiers de tests font partie du code source et ne soumettent aucune demande musicale réelle.
