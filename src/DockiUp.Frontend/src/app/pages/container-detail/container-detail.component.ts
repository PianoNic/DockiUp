import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-container-detail',
  imports: [],
  templateUrl: './container-detail.component.html',
  styleUrl: './container-detail.component.scss'
})
export class ContainerDetailComponent{
  containerId: string | null = null;

  constructor(private route: ActivatedRoute) {
    this.containerId = this.route.snapshot.paramMap.get('id');
  }
}
